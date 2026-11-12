using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Layers;

public static class LayerLoader {
  // The layer files in the directory (`*.yaml`, never `.yml`), in name order.
  public static List<string> Files(string directory)
    => [.. Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal)];

  // Reads every layer file into one tree, each value typed by its column in the schema, and refuses an insert whose
  // identity key is outside the block of its layer's depth.
  public static async Task<LayerTree> LoadAsync(string directory, SeedSchema schema, CancellationToken cancellationToken) {
    var texts = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in Files(directory)) {
      texts[Path.GetFileNameWithoutExtension(path)] = await File.ReadAllTextAsync(path, cancellationToken);
    }
    return Load(texts, schema);
  }

  // The same from the text of each layer by name, which is how a snapshot of the layers somewhere else (a commit) is read.
  public static LayerTree Load(Dictionary<string, string> texts, SeedSchema schema) {
    var layers = new List<SeedLayer>(texts.Count);
    foreach (var (name, yaml) in texts.OrderBy(entry => entry.Key, StringComparer.Ordinal)) {
      layers.Add(LayerParser.Parse(name, yaml, schema));
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
