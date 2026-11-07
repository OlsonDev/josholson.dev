namespace JoshOlson.Database.Layers;

public static class LayerLoader {
  // The layer files in the directory (`*.yaml`, never `.yml`), in name order.
  public static List<string> Files(string directory)
    => [.. Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal)];

  // Reads every layer file into one tree, each value typed by its column in the schema.
  public static async Task<LayerTree> LoadAsync(string directory, SeedSchema schema, CancellationToken cancellationToken) {
    var layers = new List<SeedLayer>();
    foreach (var path in Files(directory)) {
      var yaml = await File.ReadAllTextAsync(path, cancellationToken);
      layers.Add(LayerParser.Parse(Path.GetFileNameWithoutExtension(path), yaml, schema));
    }
    return new(layers);
  }
}
