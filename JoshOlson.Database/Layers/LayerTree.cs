namespace JoshOlson.Database.Layers;

// The layers as a tree: each names its parent with `extends`, and a layer with none is a root. Building the tree is
// where a duplicate name, a missing parent, or a cycle is found.
public sealed class LayerTree {
  private readonly Dictionary<string, SeedLayer> _layers = new(StringComparer.Ordinal);
  private readonly Dictionary<string, List<SeedLayer>> _children = new(StringComparer.Ordinal);

  public LayerTree(List<SeedLayer> layers) {
    foreach (var layer in layers) {
      if (!_layers.TryAdd(layer.Name, layer)) {
        throw new SeedFormatException($"Two layers are named '{layer.Name}'.");
      }
    }
    foreach (var layer in _layers.Values.OrderBy(layer => layer.Name, StringComparer.Ordinal)) {
      if (layer.Extends is null) {
        Roots.Add(layer);
        continue;
      }
      if (!_layers.ContainsKey(layer.Extends)) {
        throw new SeedFormatException($"{layer.Name} extends '{layer.Extends}', which does not exist.");
      }
      if (!_children.TryGetValue(layer.Extends, out var siblings)) {
        _children[layer.Extends] = siblings = [];
      }
      siblings.Add(layer);
    }
    foreach (var layer in _layers.Values) {
      ChainTo(layer.Name);
    }
  }

  public List<SeedLayer> Roots { get; } = [];

  public int Count
    => _layers.Count;

  public SeedLayer Get(string name)
    => _layers.TryGetValue(name, out var layer)
      ? layer
      : throw new SeedFormatException($"There is no layer named '{name}'.");

  public List<SeedLayer> ChildrenOf(string name)
    => _children.TryGetValue(name, out var children) ? children : [];

  // The layers to apply, root first, ending with the named layer.
  public List<SeedLayer> ChainTo(string name) {
    var chain = new List<SeedLayer>();
    var current = Get(name);
    while (true) {
      if (chain.Contains(current)) {
        throw new SeedFormatException($"Layers extend each other in a cycle: {string.Join(" -> ", chain.Select(layer => layer.Name))} -> {current.Name}.");
      }
      chain.Add(current);
      if (current.Extends is null) {
        break;
      }
      current = Get(current.Extends);
    }
    chain.Reverse();
    return chain;
  }

  public int DepthOf(string name)
    => ChainTo(name).Count - 1;

  // Every layer, each after its parent, siblings by name: the order templates are baked in.
  public List<SeedLayer> InBakeOrder() {
    var ordered = new List<SeedLayer>(_layers.Count);
    var pending = new Stack<SeedLayer>(Roots.AsEnumerable().Reverse());
    while (pending.TryPop(out var layer)) {
      ordered.Add(layer);
      foreach (var child in ChildrenOf(layer.Name).AsEnumerable().Reverse()) {
        pending.Push(child);
      }
    }
    return ordered;
  }
}
