using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Layers;

public static class LayerLoader {
  // The layer files in the directory (`*.yaml`, never `.yml`), in name order.
  public static List<string> Files(string directory)
    => [.. Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal)];

  // Reads every layer file into one tree, each value typed by its column in the schema, and refuses an insert whose
  // identity key is outside the block of its layer's depth.
  public static async Task<LayerTree> LoadAsync(string directory, SeedSchema schema, CancellationToken cancellationToken) {
    var layers = new List<SeedLayer>();
    foreach (var path in Files(directory)) {
      var yaml = await File.ReadAllTextAsync(path, cancellationToken);
      layers.Add(LayerParser.Parse(Path.GetFileNameWithoutExtension(path), yaml, schema));
    }
    var tree = new LayerTree(layers);
    var problems = tree.IdBlockProblems(schema);
    return problems.Count is 0
      ? tree
      : throw new SeedFormatException(problems.Count is 1
        ? problems[0]
        : $"The layers have {problems.Count.ToInvariantString()} ID block problems:\n- {string.Join("\n- ", problems)}");
  }
}
