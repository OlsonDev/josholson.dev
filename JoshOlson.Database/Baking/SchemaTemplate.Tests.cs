using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// `NameAsync` tests need no database. The workspace has one small migration, and scripts that do nothing in place of
// the real ones. Only a test that asserts a template was built, or that a failed build leaves nothing, puts a GUID in
// a migration so the template is new every run, and drops what it built. The test of a skipped repeatable builds from
// fixed files, so its two templates are built by the first run and stay on the server until `seed:clean`. The
// templates for the real migrations are `SchemaTemplateRealTests`.
public sealed class SchemaTemplateTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedWorkspace _workspace = SeedWorkspace.Empty("schema-template");

  public SchemaTemplateTests() {
    _workspace.WriteMigration("V001__foo.sql", "CREATE TABLE foo (bar INT);\n");
    File.WriteAllText(_workspace.Paths.ProvisioningScript, "SELECT 1;\n");
    File.WriteAllText(_workspace.Paths.SeedSchemaScript, "SELECT 4;\n");
  }

  public void Dispose()
    => _workspace.Dispose();

  private SchemaTemplate Template()
    => _workspace.Template;

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

    await _workspace.WriteMigrationAsync("V001__foo.sql", "CREATE TABLE foo (baz INT);\n", _cancellationToken);

    Assert.NotEqual(before, await Template().NameAsync(_cancellationToken));
  }

  [Fact]
  public async Task NameAsync_RepeatableMigrationOrProvisioningChanged_ChangesTheName() {
    var before = await Template().NameAsync(_cancellationToken);

    await _workspace.WriteMigrationAsync("R__qux.sql", "SELECT 2;\n", _cancellationToken);
    var withRepeatable = await Template().NameAsync(_cancellationToken);
    await File.WriteAllTextAsync(_workspace.Paths.ProvisioningScript, "SELECT 3;\n", _cancellationToken);

    Assert.NotEqual(before, withRepeatable);
    Assert.NotEqual(withRepeatable, await Template().NameAsync(_cancellationToken));
  }

  [Fact]
  public async Task NameAsync_SeedSchemaScriptChanged_ChangesTheName() {
    var before = await Template().NameAsync(_cancellationToken);

    await File.WriteAllTextAsync(_workspace.Paths.SeedSchemaScript, "SELECT 5;\n", _cancellationToken);

    Assert.NotEqual(before, await Template().NameAsync(_cancellationToken));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("seed_Foo")]
  [InlineData("seed_foo\"; DROP DATABASE bar; --")]
  public void Quote_NotASeedToolName_Throws(string name)
    => _ = Assert.Throws<SeedFormatException>(() => SeedDatabaseNames.Quote(name));

  [Fact]
  public async Task EnsureAsync_NewMigration_BuildsAClosedTemplateOnce() {
    await _workspace.WriteMigrationAsync(
      "V001__foo.sql",
      $"-- {Guid.NewGuid():N}\nCREATE TABLE foo (bar INT);\n",
      _cancellationToken);
    var databases = SeedTestServer.Databases;
    var template = Template();
    await using var owner = await DatabaseEnvironment.OpenOwnerAsync(_cancellationToken);
    var name = await template.NameAsync(_cancellationToken);
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
    } finally {
      await databases.DropAsync(owner, name, CancellationToken.None);
    }
  }

  [Fact]
  public async Task NameAsync_AnotherVersion_AnotherNameThatShowsTheVersion() {
    await _workspace.WriteMigrationAsync("V002__bar.sql", "CREATE TABLE bar (baz INT);\n", _cancellationToken);

    var one = await Template().NameAsync(1, _cancellationToken);
    var two = await Template().NameAsync(2, _cancellationToken);

    Assert.NotEqual(one, two);
    Assert.StartsWith("seed_schema_v1_", one, StringComparison.Ordinal);
    Assert.StartsWith("seed_schema_v2_", two, StringComparison.Ordinal);
    Assert.Equal(two, await Template().NameAsync(_cancellationToken));
  }

  [Fact]
  public async Task NameAsync_MigrationAfterTheVersionChanges_DoesNotChangeTheNameForThatVersion() {
    await _workspace.WriteMigrationAsync("V002__bar.sql", "CREATE TABLE bar (baz INT);\n", _cancellationToken);
    var before = await Template().NameAsync(1, _cancellationToken);

    await _workspace.WriteMigrationAsync("V002__bar.sql", "CREATE TABLE bar (qux INT);\n", _cancellationToken);

    Assert.Equal(before, await Template().NameAsync(1, _cancellationToken));
  }

  [Fact]
  public async Task NameAsync_VersionBelowEveryMigration_IsRefused()
    => _ = await Assert.ThrowsAsync<SeedFormatException>(() => Template().NameAsync(0, _cancellationToken));

  // The workspace's migration and two more, in the `tools` container's reach through the template: V001 makes a
  // table, V002 a second one, and then the repeatable. The files are the same on every run unless `marker` is given,
  // a comment for V001 that makes the templates new.
  private async Task<SchemaTemplate> FakeTemplateAsync(string repeatable, string? marker = null) {
    if (marker is not null) {
      var foo = $"-- {marker}\nCREATE TABLE foo (bar INT);\n";
      await _workspace.WriteMigrationAsync("V001__foo.sql", foo, _cancellationToken);
    }
    await _workspace.WriteMigrationAsync("V002__bar.sql", "CREATE TABLE bar (baz INT);\n", _cancellationToken);
    await _workspace.WriteMigrationAsync("R__qux.sql", repeatable, _cancellationToken);
    return Template();
  }

  // The repeatable fills the second table, so it cannot apply at version 1. Nothing here is about the building, so
  // the two templates are the same on every run and are left on the server.
  [Fact]
  public async Task EnsureAsync_RepeatableThatCannotApplyAtAnOlderVersion_IsSkippedAndNoted() {
    var template = await FakeTemplateAsync("INSERT INTO bar (baz) VALUES (1);\n");
    var databases = SeedTestServer.Databases;
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    var (old, _) = await template.EnsureAsync(owner, 1, _cancellationToken);
    var (newest, _) = await template.EnsureAsync(owner, _cancellationToken);
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
  }

  // The scripts Waypoint recorded for a template's clone, with whether each succeeded folded in: a failed one is listed
  // as `FAILED <script>`.
  private async Task<List<string>> HistoryOfAsync(ServerDatabases databases, NpgsqlConnection owner, string template) {
    var clone = $"{SeedDatabaseNames.Prefix}history_{Guid.NewGuid():N}";
    await databases.CloneAsync(owner, template, clone, _cancellationToken);
    try {
      await using var connection = await DatabaseEnvironment.OpenOwnerAsync(clone, _cancellationToken);
      return await connection.QueryAsync<string>(
        "SELECT CASE WHEN success THEN script ELSE 'FAILED ' || script END FROM public.waypoint_schema_history ORDER BY installed_rank;",
        _cancellationToken);
    } finally {
      await databases.DropAsync(owner, clone, CancellationToken.None);
    }
  }

  [Fact]
  public async Task EnsureAsync_RepeatableThatFailsAtTheNewestVersion_FailsTheBuildAndLeavesNothing() {
    var template = await FakeTemplateAsync("INSERT INTO nope (baz) VALUES (1);\n", Guid.NewGuid().ToString("N"));
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    var name = await template.NameAsync(_cancellationToken);

    _ = await Assert.ThrowsAsync<InvalidOperationException>(() => template.EnsureAsync(owner, _cancellationToken));

    var databases = SeedTestServer.Databases;
    Assert.False(await databases.ExistsAsync(owner, name, _cancellationToken));
    Assert.False(await databases.ExistsAsync(owner, SeedDatabaseNames.Building(name), _cancellationToken));
  }
}
