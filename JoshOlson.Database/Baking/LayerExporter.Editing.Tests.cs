using Xunit;

namespace JoshOlson.Database.Baking;

// Exports of a database that was built for editing a layer, which replace that layer. They are apart from
// `LayerExporterTests` so the two classes run side by side: an export clones a template two to four times.
public sealed class LayerExporterEditingTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private ExportHarness _exports = null!;

  public async ValueTask InitializeAsync()
    => _exports = await ExportHarness.OpenAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _exports.DisposeAsync();

  // Built from the child for editing it, with a tag at an ID in the child's own block. Exported with no name it is the
  // child again, with another name a sibling of the child, which needs a description of its own.
  [Fact]
  public async Task ExportAsync_BuiltForEditing_ReplacesTheLayerKeepingItsDescriptionOrIsASiblingUnderAnotherName() {
    var database = await _exports.DatabaseFromAsync(FixtureSeeds.Child, editing: true);
    await _exports.RunAsync(database, FixtureSeeds.InsertTagWithId(1020, "Zed"));

    var replacement = await _exports.ExportAsync(database, name: null, description: null);
    var sibling = await _exports.ExportAsync(database);
    var noDescription = await _exports.RefusalAsync(database, description: null);

    Assert.Equal(FixtureSeeds.Child, replacement.Layer.Name);
    Assert.Equal(FixtureSeeds.Root, replacement.Layer.Extends);
    Assert.Equal(_exports.Tool.Tree.Get(FixtureSeeds.Child).Description, replacement.Layer.Description);
    Assert.True(replacement.Replaces);
    Assert.Contains(FixtureSeeds.TagRow(1020, "Zed"), replacement.Yaml, StringComparison.Ordinal);
    Assert.Contains("{ tag_id: 1001,", replacement.Yaml, StringComparison.Ordinal);
    Assert.Equal("foo-bar", sibling.Layer.Name);
    Assert.Equal(FixtureSeeds.Root, sibling.Layer.Extends);
    Assert.False(sibling.Replaces);
    Assert.Contains("needs a description", noDescription, StringComparison.Ordinal);
  }

  // Built from the root for editing it, and the child extends the root.
  // 1. A tag renamed: the child still applies, so the root is replaced.
  // 2. A tag the child makes too, at another ID: the merge cannot see a unique constraint, the database can.
  // 3. That tag gone again, and the tag the child gives a color deleted: the child no longer merges.
  [Fact]
  public async Task ExportAsync_ReplacingALayerThatOthersExtend_IsFineWhileTheyStillApplyAndNamesTheOnesItWouldBreak() {
    var database = await _exports.DatabaseFromAsync(FixtureSeeds.Root, editing: true);

    await _exports.RunAsync(
      database,
      """
      UPDATE tag.tag AS t SET
          name  = 'Corge'
      WHERE t.tag_id = 1;
      """);
    var export = await _exports.ExportAsync(database, name: null, description: null);
    await _exports.RunAsync(database, FixtureSeeds.InsertTagWithId(900, "Xyzzy"));
    var doesNotBuild = await _exports.RefusalAsync(database, name: null, description: null);
    await _exports.RunAsync(
      database,
      """
      DELETE FROM tag.tag AS t
      WHERE t.tag_id IN (2, 900);
      """);
    var noLongerApplies = await _exports.RefusalAsync(database, name: null, description: null);

    Assert.Equal(FixtureSeeds.Root, export.Layer.Name);
    Assert.Null(export.Layer.Extends);
    Assert.True(export.Replaces);
    Assert.Contains(
      $"{FixtureSeeds.Child} does not build: Inserting into tag.tag failed",
      doesNotBuild,
      StringComparison.Ordinal);
    Assert.Contains($"{FixtureSeeds.Child} no longer applies", noLongerApplies, StringComparison.Ordinal);
  }
}
