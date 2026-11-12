using JoshOlson.Shared.IO;
using Xunit;

namespace JoshOlson.Database.Layers;

public sealed class LayerLoaderTests : IDisposable {
  private readonly string _directory = TempDirectory.Create("layers");
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  [Fact]
  public async Task LoadAsync_ReadsYamlFilesAndIgnoresOthers() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "base.yaml"), "description: Root", _cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(_directory, "foo.yaml"), "description: Child\nextends: base", _cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(_directory, "ignored.yml"), "not: a layer", _cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(_directory, "notes.md"), "# notes", _cancellationToken);

    var tree = await LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken);

    Assert.Equal(["base", "foo"], tree.InBakeOrder().Select(layer => layer.Name));
  }

  [Fact]
  public async Task LoadAsync_EmptyDirectory_GivesAnEmptyTree() {
    var tree = await LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken);

    Assert.Equal(0, tree.Count);
  }

  [Fact]
  public async Task LoadAsync_BadFile_ThrowsNamingTheLayer() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "foo.yaml"), "bar: baz", _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken));

    Assert.StartsWith("foo:", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task LoadAsync_ValueThatIsNotItsColumnsType_Throws() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "foo.yaml"), "description: Foo\ntables:\n  foo.tag:\n    update:\n      - { tag_id: 1, price: x }", _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken));

    Assert.Equal("foo: foo.tag update row 1: price must be a number, not 'x'.", exception.Message);
  }

  private async Task WriteAsync(string name, string yaml)
    => await File.WriteAllTextAsync(Path.Combine(_directory, name + ".yaml"), yaml, _cancellationToken);

  [Fact]
  public async Task LoadAsync_InsertsInsideTheBlockOfTheirDepth_Load() {
    await WriteAsync("base", $"description: Root\ntables:\n  foo.tag:\n    insert:\n      - {SampleLayers.Tag(7)}\n      - {SampleLayers.Tag(1000)}");
    await WriteAsync("child", $"description: Child\nextends: base\ntables:\n  foo.tag:\n    insert:\n      - {SampleLayers.Tag(1001)}\n      - {SampleLayers.Tag(2000)}");

    var tree = await LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken);

    Assert.Equal(2, tree.Count);
  }

  [Fact]
  public async Task LoadAsync_InsertOutsideTheBlockOfItsDepth_NamesTheLayerTableAndId() {
    await WriteAsync("base", $"description: Root\ntables:\n  foo.tag:\n    insert:\n      - {SampleLayers.Tag(7)}");
    await WriteAsync("child", $"description: Child\nextends: base\ntables:\n  foo.tag:\n    insert:\n      - {SampleLayers.Tag(8)}");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken));

    Assert.Equal("child: foo.tag inserts tag_id 8, outside the 1001..2000 block of a layer at depth 1.", exception.Message);
  }

  [Fact]
  public async Task LoadAsync_ManyInsertsOutsideTheirBlocks_AreReportedTogether() {
    await WriteAsync("base", $"description: Root\ntables:\n  foo.tag:\n    insert:\n      - {SampleLayers.Tag(1001)}");
    await WriteAsync("child", $"description: Child\nextends: base\ntables:\n  foo.tag:\n    insert:\n      - {SampleLayers.Tag(2001)}");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken));

    Assert.Equal(
      """
      The layers have 2 ID block problems:
      - base: foo.tag inserts tag_id 1001, outside the 1..1000 block of a layer at depth 0.
      - child: foo.tag inserts tag_id 2001, outside the 1001..2000 block of a layer at depth 1.
      """,
      exception.Message.Replace("\r", "", StringComparison.Ordinal));
  }

  [Fact]
  public async Task LoadAsync_UpdateOfAnAncestorsRow_IsNotHeldToTheBlock() {
    await WriteAsync("base", $"description: Root\ntables:\n  foo.tag:\n    insert:\n      - {SampleLayers.Tag(7)}");
    await WriteAsync("child", "description: Child\nextends: base\ntables:\n  foo.tag:\n    update:\n      - { tag_id: 7, price: 1 }");

    var tree = await LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken);

    Assert.Equal(2, tree.Count);
  }

  [Fact]
  public async Task LoadAsync_TableWithoutASingleIdentityKey_IsNotHeldToTheBlock() {
    // `foo.link` has a composite key, so there is no one ID to put in a block.
    await WriteAsync("base", "description: Root");
    await WriteAsync("child", $"description: Child\nextends: base\ntables:\n  foo.link:\n    insert:\n      - {SampleLayers.Link(5, 9)}");

    var tree = await LayerLoader.LoadAsync(_directory, SampleLayers.Schema, _cancellationToken);

    Assert.Equal(2, tree.Count);
  }

  [Fact]
  public void Files_ListsYamlOnly() {
    File.WriteAllText(Path.Combine(_directory, "foo.yaml"), "x");
    File.WriteAllText(Path.Combine(_directory, "bar.yml"), "x");

    Assert.Equal(["foo.yaml"], LayerLoader.Files(_directory).Select(Path.GetFileName));
  }
}
