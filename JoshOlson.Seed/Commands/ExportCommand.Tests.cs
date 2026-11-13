using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Builds databases of its own from the real layers and exports them into a directory of its own: each test copies the real
// migrations, lock, template scripts, and layers, so the template names match the real ones and the real `seeds/` is never
// written to. Only `ComposeTools` keeps the real root, for the compose files.
public sealed class ExportCommandTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedWorkspace _workspace = SeedWorkspace.CopyOfRepository("export-command");
  private readonly ServerDatabases _databases = SeedTestServer.Databases;
  private readonly List<string> _created = [];
  private NpgsqlConnection _owner = null!;

  public async ValueTask InitializeAsync()
    => _owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);

  public async ValueTask DisposeAsync() {
    foreach (var name in _created) {
      await _databases.DropAsync(_owner, name, CancellationToken.None);
    }
    await _owner.DisposeAsync();
    _workspace.Dispose();
  }

  private async Task<string> DatabaseFromAsync(string layer) {
    var tool = await _workspace.OpenToolAsync(_cancellationToken);
    var name = await tool.CreateTestDatabaseAsync(_owner, Guid.NewGuid().ToString("N"), layer, _cancellationToken);
    _created.Add(name);
    return name;
  }

  private async Task RunSqlAsync(string database, string sql) {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, _cancellationToken);
    await connection.ExecuteAsync(sql, _cancellationToken);
  }

  private async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    => await SeedAppRunner.RunAsync(_workspace, arguments);

  private string LayerFile(string name)
    => _workspace.Layer(name);

  // A real layer's text with its description line replaced, which is all a copy exported from its database should differ by.
  private static string WithDescription(string yaml, string description)
    => string.Join('\n', [$"description: {description}", .. yaml.Split('\n').Skip(1)]);

  [Fact]
  public async Task Execute_ChildOfALayer_WritesTheFileAndListsWhatItFound() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunSqlAsync(database, "INSERT INTO tag.tag (name, slug) VALUES ('Zed', 'zed'); UPDATE tag.tag SET name = 'Databases' WHERE tag_id = 1;");

    var (exitCode, output) = await RunAsync("export", "--name", "foo-bar", "--description", "Foo bar", "--database", database);

    Assert.True(exitCode is 0, output);
    Assert.Contains("tag.tag", output, StringComparison.Ordinal);
    Assert.Contains("Wrote", output, StringComparison.Ordinal);
    Assert.Equal(
      $$"""
      description: Foo bar
      extends: minimal
      tables:
        tag.tag:
          insert:
            - {{FixtureSeeds.TagRow(1001, "Zed")}}
          update:
            - { tag_id: 1, name: Databases }

      """,
      await File.ReadAllTextAsync(LayerFile("foo-bar"), _cancellationToken));
  }

  [Fact]
  public async Task Execute_NoParent_ExtendsTheLayerTheDatabaseWasBuiltFrom() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunSqlAsync(database, FixtureSeeds.InsertTag("Zed"));

    var (exitCode, output) = await RunAsync("export", "--name", "foo-bar", "--description", "Foo bar", "--database", database);

    Assert.True(exitCode is 0, output);
    Assert.StartsWith("description: Foo bar\nextends: minimal\n", await File.ReadAllTextAsync(LayerFile("foo-bar"), _cancellationToken), StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_ADatabaseBuiltFromALayer_ReproducesTheLayersFileByteForByte() {
    var database = await DatabaseFromAsync(SeedLayers.MoreTags);
    var original = await File.ReadAllTextAsync(LayerFile(SeedLayers.MoreTags), _cancellationToken);
    // Recorded as built from `minimal`, the layer `more-tags` extends, so the copy is the difference from its template.
    await RunSqlAsync(database, "UPDATE seed.hydration SET layer = 'minimal';");
    var (exitCode, output) = await RunAsync("export", "--name", "more-tags-copy", "--description", "Copy", "--database", database);

    Assert.True(exitCode is 0, output);
    Assert.Equal(
      WithDescription(original, "Copy"),
      await File.ReadAllTextAsync(LayerFile("more-tags-copy"), _cancellationToken));
  }

  [Fact]
  public async Task Execute_DatabaseThatRecordsNoLayer_IsARootDiffedFromTheSchemaTemplate() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    var original = await File.ReadAllTextAsync(LayerFile(SeedLayers.Minimal), _cancellationToken);
    await RunSqlAsync(database, "UPDATE seed.hydration SET layer = NULL;");

    var (exitCode, output) = await RunAsync("export", "--name", "minimal-copy", "--description", "Copy", "--database", database);

    Assert.True(exitCode is 0, output);
    Assert.Equal(
      WithDescription(original, "Copy"),
      await File.ReadAllTextAsync(LayerFile("minimal-copy"), _cancellationToken));
  }

  [Fact]
  public async Task Execute_NameOfALayerThatExists_IsRefusedUnlessOverwriteIsGiven() {
    var first = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunSqlAsync(first, FixtureSeeds.InsertTag("Zed"));
    var second = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunSqlAsync(second, FixtureSeeds.InsertTag("Quux"));
    _ = await RunAsync("export", "--name", "foo-bar", "--description", "Foo bar", "--database", first);

    var (refusedCode, refused) = await RunAsync("export", "--name", "foo-bar", "--description", "Foo bar", "--database", second);
    var (overwrittenCode, overwritten) = await RunAsync("export", "--name", "foo-bar", "--description", "Foo bar", "--database", second, "--overwrite");

    Assert.Equal(1, refusedCode);
    Assert.Contains("already a layer named 'foo-bar'", refused, StringComparison.Ordinal);
    Assert.True(overwrittenCode is 0, overwritten);
    Assert.Contains("quux", await File.ReadAllTextAsync(LayerFile("foo-bar"), _cancellationToken), StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_ExportedAgain_ReplacesTheLayerItMadeAndKeepsItsDescription() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunSqlAsync(database, FixtureSeeds.InsertTag("Zed"));
    _ = await RunAsync("export", "--name", "foo-bar", "--description", "Foo bar", "--database", database);
    await RunSqlAsync(database, FixtureSeeds.InsertTag("Quux"));

    var (exitCode, output) = await RunAsync("export", "--database", database);

    Assert.True(exitCode is 0, output);
    Assert.Contains("Replaced", output, StringComparison.Ordinal);
    var yaml = await File.ReadAllTextAsync(LayerFile("foo-bar"), _cancellationToken);
    Assert.StartsWith("description: Foo bar\nextends: minimal\n", yaml, StringComparison.Ordinal);
    Assert.Contains("zed", yaml, StringComparison.Ordinal);
    Assert.Contains("quux", yaml, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_BuiltAsAParentNotForEditing_NeedsAName() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunSqlAsync(database, FixtureSeeds.InsertTag("Zed"));

    var (exitCode, output) = await RunAsync("export", "--description", "Foo bar", "--database", database);

    Assert.Equal(1, exitCode);
    Assert.Contains("Give the new layer a --name", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NothingChanged_SaysSoAndWritesNothing() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);

    var (exitCode, output) = await RunAsync("export", "--name", "foo-bar", "--description", "Foo bar", "--database", database);

    Assert.Equal(1, exitCode);
    Assert.Contains("There is nothing to export", output, StringComparison.Ordinal);
    Assert.False(File.Exists(LayerFile("foo-bar")));
  }
}
