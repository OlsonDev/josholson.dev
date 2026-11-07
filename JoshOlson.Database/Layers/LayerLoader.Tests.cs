using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Layers;

public sealed class LayerLoaderTests : IDisposable {
  private static readonly SeedSchema _Schema = new(SchemaLockSample.Create());

  private readonly string _directory = Directory.CreateTempSubdirectory("layers-").FullName;
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  [Fact]
  public async Task LoadAsync_ReadsYamlFilesAndIgnoresOthers() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "base.yaml"), "description: Root", _cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(_directory, "foo.yaml"), "description: Child\nextends: base", _cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(_directory, "ignored.yml"), "not: a layer", _cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(_directory, "notes.md"), "# notes", _cancellationToken);

    var tree = await LayerLoader.LoadAsync(_directory, _Schema, _cancellationToken);

    Assert.Equal(["base", "foo"], tree.InBakeOrder().Select(layer => layer.Name));
  }

  [Fact]
  public async Task LoadAsync_EmptyDirectory_GivesAnEmptyTree() {
    var tree = await LayerLoader.LoadAsync(_directory, _Schema, _cancellationToken);

    Assert.Equal(0, tree.Count);
  }

  [Fact]
  public async Task LoadAsync_BadFile_ThrowsNamingTheLayer() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "foo.yaml"), "bar: baz", _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => LayerLoader.LoadAsync(_directory, _Schema, _cancellationToken));

    Assert.StartsWith("foo:", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task LoadAsync_ValueThatIsNotItsColumnsType_Throws() {
    await File.WriteAllTextAsync(Path.Combine(_directory, "foo.yaml"), "description: Foo\ntables:\n  foo.tag:\n    upsert:\n      - { tag_id: x }", _cancellationToken);

    await Assert.ThrowsAsync<SeedFormatException>(() => LayerLoader.LoadAsync(_directory, _Schema, _cancellationToken));
  }

  [Fact]
  public void Files_ListsYamlOnly() {
    File.WriteAllText(Path.Combine(_directory, "foo.yaml"), "x");
    File.WriteAllText(Path.Combine(_directory, "bar.yml"), "x");

    Assert.Equal(["foo.yaml"], LayerLoader.Files(_directory).Select(Path.GetFileName));
  }
}
