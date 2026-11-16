using JoshOlson.Shared.Results;

namespace JoshOlson.Database.Layers;

public static class LayerLoader {
  // The layer files in the directory (`*.yaml`, never `.yml`), in name order.
  public static List<string> Files(string directory)
    => [.. Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal)];

  // The file a layer of this name is, or would be, in the directory.
  public static string PathOf(string seedsDirectory, string name)
    => Path.Combine(seedsDirectory, name + ".yaml");

  // Reads every layer file into one tree, each value typed by its column in the schema, and refuses an insert whose
  // identity key is outside the block of its layer's depth. Returns the tree, or what is wrong with the layers: the
  // problems of the first layer that does not read, a tree that is not sound, or every insert outside its block.
  public static async Task<Result<LayerTree, SeedProblems>> LoadAsync(string directory, SeedSchema schema, CancellationToken cancellationToken) {
    var texts = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in Files(directory)) {
      texts[Path.GetFileNameWithoutExtension(path)] = await File.ReadAllTextAsync(path, cancellationToken);
    }
    return Load(texts, schema);
  }

  // The same from the text of each layer by name, which is how a snapshot of the layers somewhere else (a commit) is read.
  public static Result<LayerTree, SeedProblems> Load(Dictionary<string, string> texts, SeedSchema schema) {
    var layers = new List<SeedLayer>(texts.Count);
    foreach (var (name, yaml) in texts.OrderBy(entry => entry.Key, StringComparer.Ordinal)) {
      var parsed = LayerParser.Parse(name, yaml, schema);
      if (parsed is SeedProblems unread) {
        return unread;
      }
      layers.Add(parsed.OrThrow());
    }
    LayerTree tree;
    try {
      tree = new(schema, layers);
    } catch (SeedFormatException exception) {
      return SeedProblems.Of(exception.Message);
    }
    var problems = new SeedProblems("The layers have", "ID block problems");
    problems.AddRange(tree.IdBlockProblems());
    return problems.Any ? problems : tree;
  }
}
