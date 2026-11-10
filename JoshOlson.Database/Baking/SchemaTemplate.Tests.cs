using JoshOlson.Shared.Data;
using JoshOlson.Shared.IO;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// `NameAsync` tests need no database. The `EnsureAsync` test builds a real template, so the dev database and its
// containers must be running (`podman compose ... up -d`) with `.env` loaded (mise does that); it drops the template
// when it ends.
[Collection(TemplateCollection.Name)]
public sealed class SchemaTemplateTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = Path.Combine(Path.GetTempPath(), $"schema-template-{Guid.NewGuid():N}");

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
      new(RepositoryRoot.Find()));

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
    // Waypoint applies the real migrations from the `tools` container, whatever directory the options name, so a copy of
    // them with one extra file gives a template name of this test's own: the real template is neither dropped nor built
    // under it.
    var root = RepositoryRoot.Find();
    var options = SchemaTemplateOptions.ForRepository(root);
    var migrations = Path.Combine(_directory, "real-migrations");
    _ = Directory.CreateDirectory(migrations);
    foreach (var file in Directory.EnumerateFiles(options.MigrationsDirectory, "*.sql")) {
      File.Copy(file, Path.Combine(migrations, Path.GetFileName(file)));
    }
    await File.WriteAllTextAsync(Path.Combine(migrations, "R__unique.sql"), $"-- {Guid.NewGuid():N}\n", _cancellationToken);
    var template = new SchemaTemplate(options with { MigrationsDirectory = migrations }, new(root));
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
      Assert.False(await SchemaTemplate.ExistsAsync(owner, SeedDatabaseNames.Building(name), _cancellationToken));

      await new DatabaseCloner().CloneAsync(owner, name, clone, _cancellationToken);
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(clone));
      await connection.OpenAsync(_cancellationToken);
      var hydrationColumns = await connection.QuerySingleAsync<long>(
        "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'seed' AND table_name = 'hydration';",
        null,
        _cancellationToken);
      var apiSeesSeedSchema = await connection.QuerySingleAsync<bool>(
        "SELECT has_schema_privilege('josholson_api', 'seed', 'USAGE');",
        null,
        _cancellationToken);
      var apiSeesTags = await connection.QuerySingleAsync<bool>(
        "SELECT has_table_privilege('josholson_api', 'tag.tag', 'SELECT');",
        null,
        _cancellationToken);
      Assert.Equal(2, hydrationColumns);
      Assert.False(apiSeesSeedSchema);
      Assert.True(apiSeesTags);
    } finally {
      await SchemaTemplate.DropAsync(owner, clone, CancellationToken.None);
      await SchemaTemplate.DropAsync(owner, name, CancellationToken.None);
    }
  }
}
