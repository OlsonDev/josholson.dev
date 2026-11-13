using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Each test builds a database of its own from the real layers, changes it the way the app would, and exports it.
public sealed class SeedToolExportTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ServerDatabases _databases = new();
  private readonly List<string> _created = [];
  private NpgsqlConnection _owner = null!;
  private SeedTool _tool = null!;

  public async ValueTask InitializeAsync() {
    var paths = SeedPaths.ForRepository();
    _tool = await SeedTool.OpenAsync(paths, _databases, new ComposeTools(paths.Root), SystemClock.Instance, _cancellationToken);
    _owner = new(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await _owner.OpenAsync(_cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    foreach (var name in _created) {
      await _databases.DropAsync(_owner, name, CancellationToken.None);
    }
    await _owner.DisposeAsync();
  }

  private async Task<string> DatabaseFromAsync(string layer) {
    var name = await _tool.CreateTestDatabaseAsync(_owner, Guid.NewGuid().ToString("N"), layer, _cancellationToken);
    _created.Add(name);
    return name;
  }

  private async Task RunAsync(string database, string sql) {
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(database));
    await connection.OpenAsync(_cancellationToken);
    await connection.ExecuteAsync(sql, _cancellationToken);
  }

  private async Task<LayerExport> ExportAsync(string database, string? name = "foo-bar", string? description = "Foo bar", bool overwrite = false)
    => await _tool.ExportAsync(_owner, database, name, description, overwrite, _cancellationToken);

  [Fact]
  public async Task ExportAsync_ChildOfALayer_IsTheDifferenceFromItsTemplate() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(
      database,
      """
      INSERT INTO tag.tag (name, slug) VALUES ('Zed', 'zed');
      UPDATE tag.tag SET name = 'Databases' WHERE tag_id = 1;
      DELETE FROM tag.tag WHERE tag_id = 3;
      """);

    var export = await ExportAsync(database);

    Assert.Equal(
      """
      description: Foo bar
      extends: minimal
      tables:
        tag.tag:
          insert:
            - { tag_id: 1001, name: Zed, slug: zed, description: ~, color: ~ }
          update:
            - { tag_id: 1, name: Databases }
          delete:
            - { tag_id: 3 }

      """,
      export.Yaml);
    Assert.Equal("foo-bar", export.Layer.Name);
    Assert.Equal(SeedLayers.Minimal, export.Layer.Extends);
  }

  [Fact]
  public async Task ExportAsync_ARowWithARelativeTime_IsExportedAsTheOffsetFromTheDatabasesAnchor() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(
      database,
      """
      INSERT INTO auth.user (display_name, user_handle, created_utc)
      VALUES ('Foo', decode('00112233445566778899aabbccddeeff', 'hex'), (SELECT anchor_utc FROM seed.hydration) + interval '90 minutes');
      """);

    var export = await ExportAsync(database);

    Assert.Contains(
      "{ user_id: 1001, display_name: Foo, user_handle: \\x00112233445566778899aabbccddeeff, is_active: true, created_utc: 1h 30m }",
      export.Yaml,
      StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_FromTheSchemaTemplate_IsARootThatHoldsEverythingTheDatabaseHas() {
    await using var scratch = await ScratchDatabase.CreateAsync(_cancellationToken);
    await RunAsync(
      scratch.Name,
      """
      INSERT INTO seed.hydration (anchor_utc) VALUES ('2026-01-01 12:00:00+00');
      INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (1, 'Foo', 'foo'), (2, 'Bar', 'bar');
      """);

    var export = await ExportAsync(scratch.Name, name: "root-layer", description: "Root export");

    Assert.Equal(
      """
      description: Root export
      tables:
        tag.tag:
          insert:
            - { tag_id: 1, name: Foo, slug: foo, description: ~, color: ~ }
            - { tag_id: 2, name: Bar, slug: bar, description: ~, color: ~ }

      """,
      export.Yaml);
    Assert.Null(export.Layer.Extends);
  }

  [Fact]
  public async Task ExportAsync_TheLayerAppliedToItsParent_GivesTheDatabasesRowsBack() {
    var database = await DatabaseFromAsync(SeedLayers.MoreTags);
    await RunAsync(database, "INSERT INTO tag.tag (name, slug, color) VALUES ('Zed', 'zed', 'red-200'); UPDATE tag.tag SET color = NULL WHERE tag_id = 1001;");

    var export = await ExportAsync(database);

    Assert.Equal(SeedLayers.MoreTags, export.Layer.Extends);
    Assert.Contains("{ tag_id: 1001, color: ~ }", export.Yaml, StringComparison.Ordinal);
    Assert.Contains("{ tag_id: 2001, name: Zed, slug: zed, description: ~, color: red-200 }", export.Yaml, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_NothingChanged_SaysSo() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(database));

    Assert.Equal($"There is nothing to export: {database} holds the same rows as the template for minimal.", exception.Message);
  }

  [Fact]
  public async Task ExportAsync_InsertOutsideTheBlockOfItsDepth_NamesTheLayerTableAndId() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(database, "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (5000, 'Zed', 'zed');");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(database));

    Assert.Contains("foo-bar: tag.tag inserts tag_id 5000, outside the 1001..2000 block of a layer at depth 1.", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_NameOfALayerThatExists_NeedsOverwrite() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(database, "INSERT INTO tag.tag (name, slug) VALUES ('Zed', 'zed');");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(database, name: SeedLayers.MoreTags));

    Assert.Contains("already a layer named 'more-tags'", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_OverwritingTheLayerItExtends_IsACycle() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(database, "INSERT INTO tag.tag (name, slug) VALUES ('Zed', 'zed');");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => ExportAsync(database, name: SeedLayers.Minimal, overwrite: true));

    Assert.Contains("extend each other in a cycle", exception.Message, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("Foo_Bar", "Foo", "is not a layer name")]
  [InlineData("foo-bar", "  ", "needs a description")]
  public async Task ExportAsync_BadNameOrDescription_IsRefusedBeforeAnythingIsRead(string name, string description, string expected) {
    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => ExportAsync("no_such_database", name, description));

    Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_DatabaseThatRecordsALayerThatIsGone_NamesIt() {
    await using var scratch = await ScratchDatabase.CreateAsync(_cancellationToken);
    await RunAsync(scratch.Name, "INSERT INTO seed.hydration (layer, anchor_utc) VALUES ('no-such-layer', '2026-01-01 12:00:00+00');");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(scratch.Name));

    Assert.Contains("There is no layer named 'no-such-layer'", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_DatabaseWithNoHydrationRow_IsARootWithTimesMeasuredFromNow() {
    await using var scratch = await ScratchDatabase.CreateAsync(_cancellationToken);
    await RunAsync(scratch.Name, "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (1, 'Foo', 'foo');");

    var export = await ExportAsync(scratch.Name);

    Assert.Null(export.Layer.Extends);
    Assert.Contains("{ tag_id: 1, name: Foo, slug: foo, description: ~, color: ~ }", export.Yaml, StringComparison.Ordinal);
  }

  private async Task<string> EditingDatabaseFromAsync(string layer) {
    var database = await DatabaseFromAsync(layer);
    await RunAsync(database, "UPDATE seed.hydration SET editing = TRUE;");
    return database;
  }

  [Fact]
  public async Task ExportAsync_BuiltForEditingAndNoName_ReplacesTheLayerKeepingItsDescription() {
    var database = await EditingDatabaseFromAsync(SeedLayers.MoreTags);
    await RunAsync(database, "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (1020, 'Zed', 'zed');");

    var export = await ExportAsync(database, name: null, description: null);

    Assert.Equal(SeedLayers.MoreTags, export.Layer.Name);
    Assert.Equal(SeedLayers.Minimal, export.Layer.Extends);
    Assert.Equal(_tool.Tree.Get(SeedLayers.MoreTags).Description, export.Layer.Description);
    Assert.True(export.Replaces);
    Assert.Contains("{ tag_id: 1020, name: Zed, slug: zed, description: ~, color: ~ }", export.Yaml, StringComparison.Ordinal);
    Assert.Contains("{ tag_id: 1001,", export.Yaml, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_BuiltForEditingAndAnotherName_IsASiblingOfTheLayer() {
    var database = await EditingDatabaseFromAsync(SeedLayers.MoreTags);
    await RunAsync(database, "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (1020, 'Zed', 'zed');");

    var export = await ExportAsync(database);

    Assert.Equal("foo-bar", export.Layer.Name);
    Assert.Equal(SeedLayers.Minimal, export.Layer.Extends);
    Assert.False(export.Replaces);
  }

  [Fact]
  public async Task ExportAsync_BuiltForEditingAndAnotherName_NeedsADescription() {
    var database = await EditingDatabaseFromAsync(SeedLayers.MoreTags);
    await RunAsync(database, "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (1020, 'Zed', 'zed');");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(database, description: null));

    Assert.Contains("needs a description", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_NotBuiltForEditingAndNoName_AsksForOne() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(database, "INSERT INTO tag.tag (name, slug) VALUES ('Zed', 'zed');");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(database, name: null));

    Assert.Contains("Give the new layer a --name", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_ReplacingALayerThatOthersExtend_IsFineWhileTheyStillApply() {
    var database = await EditingDatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(database, "UPDATE tag.tag SET name = 'Foo' WHERE tag_id = 1;");

    var export = await ExportAsync(database, name: null, description: null);

    Assert.Equal(SeedLayers.Minimal, export.Layer.Name);
    Assert.Null(export.Layer.Extends);
    Assert.True(export.Replaces);
  }

  [Fact]
  public async Task ExportAsync_ReplacingALayerThatOthersExtend_NamesTheOnesItWouldBreak() {
    var database = await EditingDatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(database, "DELETE FROM tag.tag WHERE tag_id = 2;");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(database, name: null, description: null));

    Assert.Contains("more-tags no longer applies", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_ReplacingALayerSoThatOneBelowItBreaksAConstraint_NamesIt() {
    var database = await EditingDatabaseFromAsync(SeedLayers.Minimal);
    // `more-tags` inserts a tag with the slug `framework`. The merge cannot see a unique constraint, the database can.
    await RunAsync(database, "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (900, 'Foo', 'framework');");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => ExportAsync(database, name: null, description: null));

    Assert.Contains("more-tags does not build: Inserting into tag.tag failed", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task RecordExportAsync_MakesTheDatabaseTheLayerItExported() {
    var database = await DatabaseFromAsync(SeedLayers.Minimal);
    await RunAsync(database, "INSERT INTO tag.tag (name, slug) VALUES ('Zed', 'zed');");
    var export = await ExportAsync(database);

    await _tool.RecordExportAsync(database, export, _cancellationToken);

    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(database));
    await connection.OpenAsync(_cancellationToken);
    var layer = await connection.QuerySingleAsync<string>("SELECT layer FROM seed.hydration;", _cancellationToken);
    var editing = await connection.QuerySingleAsync<bool>("SELECT editing FROM seed.hydration;", _cancellationToken);
    Assert.Equal("foo-bar", layer);
    Assert.True(editing);
  }

  [Fact]
  public async Task RecordExportAsync_DatabaseWithNoRecord_GetsOneWithTheAnchorTheTimesWereMeasuredFrom() {
    await using var scratch = await ScratchDatabase.CreateAsync(_cancellationToken);
    await RunAsync(scratch.Name, "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (1, 'Foo', 'foo');");
    var export = await ExportAsync(scratch.Name);

    await _tool.RecordExportAsync(scratch.Name, export, _cancellationToken);

    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(scratch.Name));
    await connection.OpenAsync(_cancellationToken);
    var anchor = await connection.QuerySingleAsync<DateTime>("SELECT anchor_utc FROM seed.hydration;", _cancellationToken);
    Assert.Equal(export.Anchor.ToDateTimeUtc(), anchor);
  }
}
