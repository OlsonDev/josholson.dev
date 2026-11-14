using NodaTime;
using Xunit;

namespace JoshOlson.Database.Baking;

// `ExportAsync` reads its database and changes nothing, so each test builds one database from a fixture layer and
// exports it as often as it has something to say about that state, changing it the way the app would in between. The
// comment on a test is the states it walks through. Exports of a database built for editing a layer are
// `SeedToolExportEditingTests`.
public sealed class SeedToolExportTests : IAsyncLifetime {
  // A tag made, the first renamed, the third deleted, and a user made an hour and a half after the anchor.
  private static readonly string _ChangesToTheRoot = $"""
    {FixtureSeeds.InsertTag("Zed")}

    UPDATE tag.tag AS t SET
        name  = 'Corge'
    WHERE t.tag_id = 1;

    DELETE FROM tag.tag AS t
    WHERE t.tag_id = 3;

    INSERT INTO auth.user (
        display_name
      , user_handle
      , created_utc
    ) VALUES (
        'Foo'
      , decode('00112233445566778899aabbccddeeff', 'hex')
      , (
          SELECT h.anchor_utc + INTERVAL '90 minutes'
          FROM (SELECT) AS _
          JOIN seed.hydration AS h  ON TRUE
        )
    );
    """;

  // The user as a layer writes it, a child's first row in its block.
  private const string _UserRow = "{ user_id: 1001, display_name: Foo, "
    + @"user_handle: \x00112233445566778899aabbccddeeff, is_active: true, created_utc: 1h 30m }";

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private ExportHarness _exports = null!;

  public async ValueTask InitializeAsync()
    => _exports = await ExportHarness.OpenAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _exports.DisposeAsync();

  private async Task<(Instant Anchor, string? Layer, bool Editing)> ReadHydrationAsync(string database) {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, _cancellationToken);
    return await SeedHydration.ReadAsync(connection, anchorIfUnrecorded: null, _cancellationToken);
  }

  // 1. Built from the root and unchanged: there is nothing to export.
  // 2. A tag made, one renamed, one deleted, and a user made an hour and a half after the anchor: a child of the root
  //    that is exactly those changes, which cannot take the name of another layer, nor of the root itself, and needs
  //    a name.
  // 3. Recording the export, which is the one step that writes: the database now stands for the layer it made.
  [Fact]
  public async Task ExportAsync_BuiltFromALayerAndThenChanged_IsAChildThatIsTheDifferenceFromItsTemplate() {
    var database = await _exports.DatabaseFromAsync(FixtureSeeds.Root);

    var unchanged = await _exports.RefusalAsync(database);
    await _exports.RunAsync(database, _ChangesToTheRoot);
    var export = await _exports.ExportAsync(database);
    var nameInUse = await _exports.RefusalAsync(database, name: FixtureSeeds.Child);
    var cycle = await _exports.RefusalAsync(database, name: FixtureSeeds.Root, overwrite: true);
    var noName = await _exports.RefusalAsync(database, name: null);
    await _exports.Tool.RecordExportAsync(database, export, _cancellationToken);
    var (_, layer, editing) = await ReadHydrationAsync(database);

    Assert.Equal(
      $"There is nothing to export: {database} holds the same rows as the template for {FixtureSeeds.Root}.",
      unchanged);
    Assert.Equal(
      $$"""
      description: Foo bar
      extends: {{FixtureSeeds.Root}}
      tables:
        auth.user:
          insert:
            - {{_UserRow}}
        tag.tag:
          insert:
            - {{FixtureSeeds.TagRow(1001, "Zed")}}
          update:
            - { tag_id: 1, name: Corge }
          delete:
            - { tag_id: 3 }

      """,
      export.Yaml);
    Assert.Equal("foo-bar", export.Layer.Name);
    Assert.Equal(FixtureSeeds.Root, export.Layer.Extends);
    Assert.Contains($"already a layer named '{FixtureSeeds.Child}'", nameInUse, StringComparison.Ordinal);
    Assert.Contains("extend each other in a cycle", cycle, StringComparison.Ordinal);
    Assert.Contains("Give the new layer a --name", noName, StringComparison.Ordinal);
    Assert.Equal("foo-bar", layer);
    Assert.True(editing);
  }

  // Built from the root, with a tag at an ID no layer at depth 1 may take.
  [Fact]
  public async Task ExportAsync_InsertOutsideTheBlockOfItsDepth_NamesTheLayerTableAndId() {
    var database = await _exports.DatabaseFromAsync(FixtureSeeds.Root);
    await _exports.RunAsync(database, FixtureSeeds.InsertTagWithId(5000, "Zed"));

    var refusal = await _exports.RefusalAsync(database);

    Assert.Contains(
      "foo-bar: tag.tag inserts tag_id 5000, outside the 1001..2000 block of a layer at depth 1.",
      refusal,
      StringComparison.Ordinal);
  }

  // Built from the child, with a tag made and the color the child gave a tag of the root's cleared.
  [Fact]
  public async Task ExportAsync_TheLayerAppliedToItsParent_GivesTheDatabasesRowsBack() {
    var database = await _exports.DatabaseFromAsync(FixtureSeeds.Child);
    await _exports.RunAsync(
      database,
      """
      INSERT INTO tag.tag (
          name
        , slug
        , color
      ) VALUES (
          'Zed'
        , 'zed'
        , 'red-200'
      );

      UPDATE tag.tag AS t SET
          color  = NULL
      WHERE t.tag_id = 2;
      """);

    var export = await _exports.ExportAsync(database);

    Assert.Equal(FixtureSeeds.Child, export.Layer.Extends);
    Assert.Contains("{ tag_id: 2, color: ~ }", export.Yaml, StringComparison.Ordinal);
    Assert.Contains(FixtureSeeds.TagRow(2001, "Zed", "red-200"), export.Yaml, StringComparison.Ordinal);
  }

  // A clone of the schema template, which records no layer.
  // 1. A tag and no hydration row: a root, with times measured from now.
  // 2. Recording that export gives the database a row, with the anchor the export measured from.
  // 3. The row naming a layer that is gone: refused, naming it.
  // 4. The row naming no layer, and a second tag: a root that holds everything the database has.
  [Fact]
  public async Task ExportAsync_DatabaseThatRecordsNoLayer_IsARootThatHoldsEverythingTheDatabaseHas() {
    await using var scratch = await ScratchDatabase.CreateAsync(_cancellationToken);
    await _exports.RunAsync(scratch.Name, FixtureSeeds.InsertTagWithId(1, "Foo"));

    var unrecorded = await _exports.ExportAsync(scratch.Name);
    await _exports.Tool.RecordExportAsync(scratch.Name, unrecorded, _cancellationToken);
    var recorded = await ReadHydrationAsync(scratch.Name);
    await _exports.RunAsync(
      scratch.Name,
      """
      UPDATE seed.hydration AS h SET
          layer  = 'no-such-layer';
      """);
    var layerIsGone = await _exports.RefusalAsync(scratch.Name);
    await _exports.RunAsync(
      scratch.Name,
      $"""
      UPDATE seed.hydration AS h SET
          layer    = NULL
        , editing  = FALSE;

      {FixtureSeeds.InsertTagWithId(2, "Bar")}
      """);
    var root = await _exports.ExportAsync(scratch.Name, name: "root-layer", description: "Root export");

    Assert.Null(unrecorded.Layer.Extends);
    Assert.Contains(FixtureSeeds.TagRow(1, "Foo"), unrecorded.Yaml, StringComparison.Ordinal);
    Assert.Equal((unrecorded.Anchor, "foo-bar", true), recorded);
    Assert.Contains("There is no layer named 'no-such-layer'", layerIsGone, StringComparison.Ordinal);
    Assert.Equal(
      $$"""
      description: Root export
      tables:
        tag.tag:
          insert:
            - {{FixtureSeeds.TagRow(1, "Foo")}}
            - {{FixtureSeeds.TagRow(2, "Bar")}}

      """,
      root.Yaml);
    Assert.Null(root.Layer.Extends);
  }

  [Theory]
  [InlineData("Foo_Bar", "Foo", "is not a layer name")]
  [InlineData("foo-bar", "  ", "needs a description")]
  public async Task ExportAsync_BadNameOrDescription_IsRefusedBeforeAnythingIsRead(
    string name,
    string description,
    string expected
  )
    => Assert.Contains(
      expected,
      await _exports.RefusalAsync("no_such_database", name, description),
      StringComparison.Ordinal);
}
