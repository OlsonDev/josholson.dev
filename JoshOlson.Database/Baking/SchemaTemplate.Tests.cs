using JoshOlson.Database.Locks;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Data;
using JoshOlson.Shared.IO;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// `NameAsync` tests need no database. The `EnsureAsync` test builds a real template under a name of its own and drops
// it when it ends.
[Collection(TemplateCollection.Name)]
public sealed class SchemaTemplateTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = TempDirectory.Create("schema-template");

  public SchemaTemplateTests() {
    _ = Directory.CreateDirectory(_directory);
    File.WriteAllText(Path.Combine(_directory, "V001__foo.sql"), "CREATE TABLE foo (bar INT);\n");
    File.WriteAllText(Path.Combine(_directory, "provisioning.sql"), "SELECT 1;\n");
    File.WriteAllText(Path.Combine(_directory, "seed_schema.sql"), "SELECT 4;\n");
  }

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private SchemaTemplate Template(string? migrations = null)
    => new(
      new() {
        MigrationsDirectory = migrations ?? _directory,
        ProvisioningScript = Path.Combine(_directory, "provisioning.sql"),
        SeedSchemaScript = Path.Combine(_directory, "seed_schema.sql"),
      },
      new(SeedPaths.ForRepository().Root),
      new());

  [Fact]
  public async Task NameAsync_SameFiles_SameName() {
    var first = await Template().NameAsync(_cancellationToken);
    var second = await Template().NameAsync(_cancellationToken);

    Assert.Equal(first, second);
    Assert.StartsWith(SeedDatabaseNames.Prefix, first, StringComparison.Ordinal);
    Assert.True(first.Length <= 63);
  }

  [Fact]
  public async Task NameAsync_ChangedMigration_ChangesTheName() {
    var before = await Template().NameAsync(_cancellationToken);

    await File.WriteAllTextAsync(Path.Combine(_directory, "V001__foo.sql"), "CREATE TABLE foo (baz INT);\n", _cancellationToken);

    Assert.NotEqual(before, await Template().NameAsync(_cancellationToken));
  }

  [Fact]
  public async Task NameAsync_RepeatableMigrationOrProvisioningChanged_ChangesTheName() {
    var before = await Template().NameAsync(_cancellationToken);

    await File.WriteAllTextAsync(Path.Combine(_directory, "R__qux.sql"), "SELECT 2;\n", _cancellationToken);
    var withRepeatable = await Template().NameAsync(_cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(_directory, "provisioning.sql"), "SELECT 3;\n", _cancellationToken);

    Assert.NotEqual(before, withRepeatable);
    Assert.NotEqual(withRepeatable, await Template().NameAsync(_cancellationToken));
  }

  [Fact]
  public async Task NameAsync_SeedSchemaScriptChanged_ChangesTheName() {
    var before = await Template().NameAsync(_cancellationToken);

    await File.WriteAllTextAsync(Path.Combine(_directory, "seed_schema.sql"), "SELECT 5;\n", _cancellationToken);

    Assert.NotEqual(before, await Template().NameAsync(_cancellationToken));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("seed_Foo")]
  [InlineData("seed_foo\"; DROP DATABASE bar; --")]
  public void Quote_NotASeedToolName_Throws(string name)
    => _ = Assert.Throws<SeedFormatException>(() => SeedDatabaseNames.Quote(name));

  [Fact]
  public async Task EnsureAsync_RealMigrations_BuildsAClosedTemplateOnce() {
    // A copy of the real migrations with one extra file gives a template name of this test's own: the real template is
    // neither dropped nor built under it.
    var paths = SeedPaths.ForRepository();
    var options = SchemaTemplateOptions.ForRepository(paths);
    var migrations = Path.Combine(_directory, "real-migrations");
    _ = Directory.CreateDirectory(migrations);
    foreach (var file in Directory.EnumerateFiles(options.MigrationsDirectory, "*.sql")) {
      File.Copy(file, Path.Combine(migrations, Path.GetFileName(file)));
    }
    await File.WriteAllTextAsync(Path.Combine(migrations, "R__unique.sql"), $"-- {Guid.NewGuid():N}\n", _cancellationToken);
    var databases = new ServerDatabases();
    var template = new SchemaTemplate(options with { MigrationsDirectory = migrations }, new(paths.Root), databases);
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await owner.OpenAsync(_cancellationToken);
    var name = await template.NameAsync(_cancellationToken);
    var clone = $"seed_templatetest_{Guid.NewGuid():N}";
    try {
      var first = await template.EnsureAsync(owner, _cancellationToken);
      var second = await template.EnsureAsync(owner, _cancellationToken);

      var allowConnections = await owner.QuerySingleAsync<bool>(
        "SELECT datallowconn FROM pg_database WHERE datname = @name;",
        new { name },
        _cancellationToken);
      Assert.Equal((name, true), first);
      Assert.Equal((name, false), second);
      Assert.False(allowConnections);
      Assert.False(await databases.ExistsAsync(owner, SeedDatabaseNames.Building(name), _cancellationToken));

      await databases.CloneAsync(owner, name, clone, _cancellationToken);
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(clone));
      await connection.OpenAsync(_cancellationToken);
      var hydrationColumns = await connection.QuerySingleAsync<long>(
        "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'seed' AND table_name = 'hydration';",
        _cancellationToken);
      var apiSeesSeedSchema = await connection.QuerySingleAsync<bool>(
        "SELECT has_schema_privilege('josholson_api', 'seed', 'USAGE');",
        _cancellationToken);
      var apiSeesTags = await connection.QuerySingleAsync<bool>(
        "SELECT has_table_privilege('josholson_api', 'tag.tag', 'SELECT');",
        _cancellationToken);
      Assert.Equal(4, hydrationColumns);
      Assert.False(apiSeesSeedSchema);
      Assert.True(apiSeesTags);
    } finally {
      await databases.DropAsync(owner, clone, CancellationToken.None);
      await databases.DropAsync(owner, name, CancellationToken.None);
    }
  }

  [Fact]
  public async Task NameAsync_AnotherVersion_AnotherNameThatShowsTheVersion() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "V002__bar.sql"), "CREATE TABLE bar (baz INT);\n", _cancellationToken);

    var one = await Template().NameAsync(1, _cancellationToken);
    var two = await Template().NameAsync(2, _cancellationToken);

    Assert.NotEqual(one, two);
    Assert.StartsWith("seed_schema_v1_", one, StringComparison.Ordinal);
    Assert.StartsWith("seed_schema_v2_", two, StringComparison.Ordinal);
    Assert.Equal(two, await Template().NameAsync(_cancellationToken));
  }

  [Fact]
  public async Task NameAsync_MigrationAfterTheVersionChanges_DoesNotChangeTheNameForThatVersion() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "V002__bar.sql"), "CREATE TABLE bar (baz INT);\n", _cancellationToken);
    var before = await Template().NameAsync(1, _cancellationToken);

    await File.WriteAllTextAsync(Path.Combine(_directory, "V002__bar.sql"), "CREATE TABLE bar (qux INT);\n", _cancellationToken);

    Assert.Equal(before, await Template().NameAsync(1, _cancellationToken));
  }

  [Fact]
  public async Task NameAsync_VersionBelowEveryMigration_IsRefused()
    => _ = await Assert.ThrowsAsync<SeedFormatException>(() => Template().NameAsync(0, _cancellationToken));

  // A directory of migrations of this test's own, in the `tools` container's reach through the template: V001 makes a
  // table, V002 a second one, and the repeatable fills the second, so it cannot apply at version 1.
  private SchemaTemplate FakeTemplate(string repeatable) {
    var unique = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(_directory, "V001__foo.sql"), $"-- {unique}\nCREATE TABLE foo (bar INT);\n");
    File.WriteAllText(Path.Combine(_directory, "V002__bar.sql"), "CREATE TABLE bar (baz INT);\n");
    File.WriteAllText(Path.Combine(_directory, "R__qux.sql"), repeatable);
    return Template();
  }

  [Fact]
  public async Task EnsureAsync_RepeatableThatCannotApplyAtAnOlderVersion_IsSkippedAndNoted() {
    var template = FakeTemplate("INSERT INTO bar (baz) VALUES (1);\n");
    var databases = new ServerDatabases();
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(_cancellationToken);
    var old = await template.NameAsync(1, _cancellationToken);
    var newest = await template.NameAsync(2, _cancellationToken);
    try {
      _ = await template.EnsureAsync(owner, 1, _cancellationToken);
      _ = await template.EnsureAsync(owner, _cancellationToken);
      var historyAtOne = await HistoryOfAsync(databases, owner, old);
      var historyAtTwo = await HistoryOfAsync(databases, owner, newest);

      var skippedAtOne = await template.SkippedAsync(owner, old, _cancellationToken);
      var skippedAtTwo = await template.SkippedAsync(owner, newest, _cancellationToken);
      var skipped = Assert.Single(skippedAtOne);
      Assert.Equal("R__qux.sql", skipped.File);
      Assert.Contains("relation \"bar\" does not exist", skipped.Error, StringComparison.Ordinal);
      Assert.Empty(skippedAtTwo);
      // A skipped repeatable leaves nothing in Waypoint's history, not even a failed row; one that applied is there.
      Assert.Equal(["V001__foo.sql"], historyAtOne);
      Assert.Equal(["V001__foo.sql", "V002__bar.sql", "R__qux.sql"], historyAtTwo);
    } finally {
      await databases.DropAsync(owner, old, CancellationToken.None);
      await databases.DropAsync(owner, newest, CancellationToken.None);
    }
  }

  // The scripts Waypoint recorded for a template's clone, with whether each succeeded folded in: a failed one is listed
  // as `FAILED <script>`.
  private async Task<List<string>> HistoryOfAsync(ServerDatabases databases, NpgsqlConnection owner, string template) {
    var clone = $"{SeedDatabaseNames.Prefix}history_{Guid.NewGuid():N}";
    await databases.CloneAsync(owner, template, clone, _cancellationToken);
    try {
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(clone));
      await connection.OpenAsync(_cancellationToken);
      return await connection.QueryAsync<string>(
        "SELECT CASE WHEN success THEN script ELSE 'FAILED ' || script END FROM public.waypoint_schema_history ORDER BY installed_rank;",
        _cancellationToken);
    } finally {
      await databases.DropAsync(owner, clone, CancellationToken.None);
    }
  }

  [Fact]
  public async Task EnsureAsync_RepeatableThatFailsAtTheNewestVersion_FailsTheBuildAndLeavesNothing() {
    var template = FakeTemplate("INSERT INTO nope (baz) VALUES (1);\n");
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(_cancellationToken);
    var name = await template.NameAsync(_cancellationToken);

    _ = await Assert.ThrowsAsync<InvalidOperationException>(() => template.EnsureAsync(owner, _cancellationToken));

    var databases = new ServerDatabases();
    Assert.False(await databases.ExistsAsync(owner, name, _cancellationToken));
    Assert.False(await databases.ExistsAsync(owner, SeedDatabaseNames.Building(name), _cancellationToken));
  }

  [Fact]
  public async Task EnsureAsync_OldRealVersion_HasOnlyThoseMigrationsAndIsThenMigratedToTheNewest() {
    var paths = SeedPaths.ForRepository();
    var options = SchemaTemplateOptions.ForRepository(paths);
    var migrations = Path.Combine(_directory, "real-migrations");
    _ = Directory.CreateDirectory(migrations);
    foreach (var file in Directory.EnumerateFiles(options.MigrationsDirectory, "*.sql")) {
      File.Copy(file, Path.Combine(migrations, Path.GetFileName(file)));
    }
    await File.WriteAllTextAsync(Path.Combine(migrations, "R__unique.sql"), $"-- {Guid.NewGuid():N}\n", _cancellationToken);
    var databases = new ServerDatabases();
    var tools = new ComposeTools(paths.Root);
    var template = new SchemaTemplate(options with { MigrationsDirectory = migrations }, tools, databases);
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(_cancellationToken);
    var (name, _) = await template.EnsureAsync(owner, 3, _cancellationToken);
    var clone = $"{SeedDatabaseNames.Prefix}versioned_{Guid.NewGuid():N}";
    await databases.CloneAsync(owner, name, clone, _cancellationToken);
    try {
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(clone));
      await connection.OpenAsync(_cancellationToken);
      var applied = await MigrationHistory.ReadAsync(connection, _cancellationToken);
      var skipped = await template.SkippedAsync(owner, name, _cancellationToken);
      Assert.Equal([1, 2, 3], applied.ConvertAll(migration => migration.Version));
      Assert.False(await connection.QuerySingleAsync<bool>("SELECT to_regclass('blob.mime_type') IS NOT NULL;", _cancellationToken));
      Assert.True(await connection.QuerySingleAsync<bool>("SELECT to_regclass('seed.hydration') IS NOT NULL;", _cancellationToken));
      Assert.Equal(["R__blob_reference_data.sql"], skipped.ConvertAll(repeatable => repeatable.File));

      await tools.MigrateAsync(clone, _cancellationToken);

      var onDisk = await MigrationFiles.LoadAsync(paths.Migrations, _cancellationToken);
      Assert.Empty(MigrationHistory.Check(onDisk, await MigrationHistory.ReadAsync(connection, _cancellationToken)));
      Assert.True(await connection.QuerySingleAsync<long>("SELECT count(*) FROM blob.mime_type;", _cancellationToken) > 0);
    } finally {
      await databases.DropAsync(owner, clone, CancellationToken.None);
      await databases.DropAsync(owner, name, CancellationToken.None);
    }
  }
}
