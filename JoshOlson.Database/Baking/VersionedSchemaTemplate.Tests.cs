using JoshOlson.Database.Locks;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Data;
using JoshOlson.Shared.IO;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// `NameAsync` tests need no database. The `EnsureAsync` test builds the real template for an old version and takes the
// database on to the newest one, the way `seeds:migrate` will.
[Collection(TemplateCollection.Name)]
public sealed class VersionedSchemaTemplateTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = TempDirectory.Create("versioned-schema-template");

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private static VersionedSchemaTemplate Template(string? migrations = null) {
    var paths = SeedPaths.ForRepository();
    return new(
      SchemaTemplateOptions.ForRepository(paths) with { MigrationsDirectory = migrations ?? paths.Migrations },
      new(paths.Root),
      new());
  }

  private async Task<string> CopyMigrationsAsync() {
    foreach (var file in Directory.EnumerateFiles(SeedPaths.ForRepository().Migrations, "*.sql")) {
      await File.WriteAllBytesAsync(Path.Combine(_directory, Path.GetFileName(file)), await File.ReadAllBytesAsync(file, _cancellationToken), _cancellationToken);
    }
    return _directory;
  }

  [Fact]
  public async Task NameAsync_SameVersion_SameName() {
    var first = await Template().NameAsync(3, _cancellationToken);
    var second = await Template().NameAsync(3, _cancellationToken);

    Assert.Equal(first, second);
  }

  [Fact]
  public async Task NameAsync_AnotherVersion_AnotherName() {
    var three = await Template().NameAsync(3, _cancellationToken);
    var four = await Template().NameAsync(4, _cancellationToken);

    Assert.NotEqual(three, four);
  }

  [Fact]
  public async Task NameAsync_MigrationAfterTheVersionChanges_DoesNotChangeTheName() {
    var before = await Template(await CopyMigrationsAsync()).NameAsync(3, _cancellationToken);
    await File.AppendAllTextAsync(Path.Combine(_directory, "V004__jsonb.sql"), "\n-- foo\n", _cancellationToken);
    var after = await Template(_directory).NameAsync(3, _cancellationToken);

    Assert.Equal(before, after);
  }

  [Fact]
  public async Task NameAsync_MigrationUpToTheVersionChanges_ChangesTheName() {
    var before = await Template(await CopyMigrationsAsync()).NameAsync(3, _cancellationToken);
    await File.AppendAllTextAsync(Path.Combine(_directory, "V003__color.sql"), "\n-- foo\n", _cancellationToken);
    var after = await Template(_directory).NameAsync(3, _cancellationToken);

    Assert.NotEqual(before, after);
  }

  [Fact]
  public async Task NameAsync_VersionBelowEveryMigration_IsRefused()
    => _ = await Assert.ThrowsAsync<SeedFormatException>(() => Template().NameAsync(0, _cancellationToken));

  [Fact]
  public async Task EnsureAsync_OldVersion_HasOnlyThoseMigrationsAndIsThenMigratedToTheNewest() {
    var paths = SeedPaths.ForRepository();
    var template = Template();
    var databases = new ServerDatabases();
    var tools = new ComposeTools(paths.Root);
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(_cancellationToken);
    var (name, _) = await template.EnsureAsync(owner, 3, _cancellationToken);
    var clone = $"{SeedDatabaseNames.Prefix}versioned_{Guid.NewGuid():N}";
    await databases.CloneAsync(owner, name, clone, _cancellationToken);
    try {
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(clone));
      await connection.OpenAsync(_cancellationToken);
      var applied = await MigrationHistory.ReadAsync(connection, _cancellationToken);
      var hasBlob = await connection.QuerySingleAsync<bool>("SELECT to_regclass('blob.mime_type') IS NOT NULL;", _cancellationToken);
      var hasSeedSchema = await connection.QuerySingleAsync<bool>("SELECT to_regclass('seed.hydration') IS NOT NULL;", _cancellationToken);
      Assert.Equal([1, 2, 3], applied.ConvertAll(migration => migration.Version));
      Assert.False(hasBlob);
      Assert.True(hasSeedSchema);

      await tools.MigrateAsync(clone, _cancellationToken);

      var onDisk = await MigrationFiles.LoadAsync(paths.Migrations, _cancellationToken);
      Assert.Empty(MigrationHistory.Check(onDisk, await MigrationHistory.ReadAsync(connection, _cancellationToken)));
      Assert.True(await connection.QuerySingleAsync<long>("SELECT count(*) FROM blob.mime_type;", _cancellationToken) > 0);
    } finally {
      await databases.DropAsync(owner, clone, CancellationToken.None);
    }
  }
}
