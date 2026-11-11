using JoshOlson.Database.Layers;
using JoshOlson.Shared.IO;
using NodaTime;
using Xunit;

namespace JoshOlson.Database.Baking;

// The name needs no database: it is a hash of the schema template's name and the layers' files. Building a template is
// covered by `SeedToolTests`, which bakes the real layers.
[Collection(TemplateCollection.Name)]
public sealed class LayerTemplateTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = TempDirectory.Create("layer-template");

  public LayerTemplateTests()
    => _ = Directory.CreateDirectory(_directory);

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private async Task<LayerTemplate> OpenAsync() {
    var paths = SeedPaths.ForRepository();
    var schema = await ScratchDatabase.LoadSchemaAsync(_cancellationToken);
    var layers = new List<SeedLayer>();
    foreach (var path in LayerLoader.Files(_directory)) {
      var name = Path.GetFileNameWithoutExtension(path);
      var yaml = await File.ReadAllTextAsync(path, _cancellationToken);
      layers.Add(LayerParser.Parse(name, yaml, schema));
    }
    var tree = new LayerTree(layers);
    var tools = new ComposeTools(paths.Root);
    var databases = new ServerDatabases();
    var schemaTemplate = new SchemaTemplate(SchemaTemplateOptions.ForRepository(paths), tools, databases);
    return new(schemaTemplate, databases, tree, schema, _directory, SystemClock.Instance);
  }

  private async Task WriteAsync(string name, string yaml)
    => await File.WriteAllTextAsync(Path.Combine(_directory, name + ".yaml"), yaml, _cancellationToken);

  [Fact]
  public async Task NameAsync_SameFiles_SameName() {
    await WriteAsync("foo", "description: Root\n");

    var first = await (await OpenAsync()).NameAsync("foo", _cancellationToken);
    var second = await (await OpenAsync()).NameAsync("foo", _cancellationToken);

    Assert.Equal(first, second);
    Assert.StartsWith("seed_layer_", first, StringComparison.Ordinal);
    Assert.True(first.Length <= 63);
  }

  [Fact]
  public async Task NameAsync_AnyLayerInTheChainChanged_ChangesTheName() {
    await WriteAsync("foo", "description: Root\n");
    await WriteAsync("bar", "description: Child\nextends: foo\n");
    var template = await OpenAsync();
    var child = await template.NameAsync("bar", _cancellationToken);
    var root = await template.NameAsync("foo", _cancellationToken);

    await WriteAsync("foo", "description: Root, changed\n");

    Assert.NotEqual(child, root);
    Assert.NotEqual(root, await template.NameAsync("foo", _cancellationToken));
    Assert.NotEqual(child, await template.NameAsync("bar", _cancellationToken));
  }

  [Fact]
  public async Task NameAsync_SiblingChanged_LeavesTheNameAlone() {
    await WriteAsync("foo", "description: Root\n");
    await WriteAsync("bar", "description: One\nextends: foo\n");
    await WriteAsync("baz", "description: Two\nextends: foo\n");
    var template = await OpenAsync();
    var before = await template.NameAsync("bar", _cancellationToken);

    await WriteAsync("baz", "description: Two, changed\nextends: foo\n");

    Assert.Equal(before, await template.NameAsync("bar", _cancellationToken));
  }
}
