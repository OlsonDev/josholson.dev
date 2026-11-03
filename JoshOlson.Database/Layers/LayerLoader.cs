namespace JoshOlson.Database.Layers;

public static class LayerLoader {
  // Reads every `*.yaml` file in the directory (never `.yml`) into one tree.
  public static async Task<LayerTree> LoadAsync(string directory, CancellationToken cancellationToken) {
    List<SeedLayer> layers = [];
    foreach (var path in Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal)) {
      var yaml = await File.ReadAllTextAsync(path, cancellationToken);
      layers.Add(LayerParser.Parse(Path.GetFileNameWithoutExtension(path), yaml));
    }
    return new(layers);
  }
}
