using JoshOlson.Database.Catalog;
using JoshOlson.Database.Locks;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Data;
using Xunit;

namespace JoshOlson.Database.Baking;

// The templates for the real migrations, as they are in the repository. Neither test is about the building, so the
// templates are built by the first run and stay on the server: the newest is the one every other test clones, and
// the one for version 3 stays until `seed:clean`. Each test reads a clone, which it drops.
public sealed class SchemaTemplateRealTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  // A database the seed tool builds has the `seed` schema in it, which belongs to no migration. The API role is
  // granted nothing in it, and the lock and the docs generated from a catalog must never list it: they would otherwise
  // change depending on whether `seed` had rebuilt the dev database.
  [Fact]
  public async Task EnsureAsync_RealMigrations_HasTheSeedSchemaOutOfTheApiRolesReachAndOutOfTheCatalog() {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);

    var hydrationColumns = await connection.QuerySingleAsync<long>(
      "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'seed' AND table_name = 'hydration';",
      _cancellationToken);
    var apiCanConnect = await connection.QuerySingleAsync<bool>(
      "SELECT has_database_privilege('josholson_api', current_database(), 'CONNECT');",
      _cancellationToken);
    var apiSeesSeedSchema = await connection.QuerySingleAsync<bool>(
      "SELECT has_schema_privilege('josholson_api', 'seed', 'USAGE');",
      _cancellationToken);
    var apiSeesTags = await connection.QuerySingleAsync<bool>(
      "SELECT has_table_privilege('josholson_api', 'tag.tag', 'SELECT');",
      _cancellationToken);
    var catalog = await SchemaCatalog.ReadAsync(connection, new(), _cancellationToken);
    Assert.Equal(4, hydrationColumns);
    Assert.True(apiCanConnect);
    Assert.False(apiSeesSeedSchema);
    Assert.True(apiSeesTags);
    Assert.NotEmpty(catalog.Tables);
    Assert.DoesNotContain(catalog.Tables, table => table.Name.StartsWith("seed.", StringComparison.Ordinal));
  }

  [Fact]
  public async Task EnsureAsync_OldRealVersion_HasOnlyThoseMigrationsAndIsThenMigratedToTheNewest() {
    var databases = SeedTestServer.Databases;
    var tools = SeedTestServer.Tools;
    var template = SeedTestServer.RealTemplate;
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    var (name, _) = await template.EnsureAsync(owner, 3, _cancellationToken);
    var clone = $"{SeedDatabaseNames.Prefix}versioned_{Guid.NewGuid():N}";
    await databases.CloneAsync(owner, name, clone, _cancellationToken);
    try {
      await using var connection = await DatabaseEnvironment.OpenOwnerAsync(clone, _cancellationToken);
      var applied = await MigrationHistory.ReadAsync(connection, _cancellationToken);
      var skipped = await template.SkippedAsync(owner, name, _cancellationToken);
      var hasMimeTypes = await connection.QuerySingleAsync<bool>(
        "SELECT to_regclass('blob.mime_type') IS NOT NULL;",
        _cancellationToken);
      var hasHydration = await connection.QuerySingleAsync<bool>(
        "SELECT to_regclass('seed.hydration') IS NOT NULL;",
        _cancellationToken);
      Assert.Equal([1, 2, 3], applied.ConvertAll(migration => migration.Version));
      Assert.False(hasMimeTypes);
      Assert.True(hasHydration);
      Assert.Equal(["R__blob_reference_data.sql"], skipped.ConvertAll(repeatable => repeatable.File));

      await tools.MigrateAsync(clone, _cancellationToken);

      var onDisk = await MigrationFiles.LoadAsync(SeedTestServer.Paths.Migrations, _cancellationToken);
      Assert.Empty(MigrationHistory.Check(onDisk, await MigrationHistory.ReadAsync(connection, _cancellationToken)));
      var mimeTypes = await connection.QuerySingleAsync<long>(
        "SELECT count(*) FROM blob.mime_type;",
        _cancellationToken);
      Assert.True(mimeTypes > 0);
    } finally {
      await databases.DropAsync(owner, clone, CancellationToken.None);
    }
  }
}
