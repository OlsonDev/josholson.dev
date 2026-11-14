using JoshOlson.Database.Schema;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Layers;

// The layers as a tree: each names its parent with `extends`, and a layer with none is a root. Building the tree is
// where a duplicate name, a missing parent, or a cycle is found.
public sealed class LayerTree {
  private readonly Dictionary<string, SeedLayer> _layers = [with(StringComparer.Ordinal)];
  private readonly Dictionary<string, List<SeedLayer>> _children = [with(StringComparer.Ordinal)];

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

  // The rows a layer inserts take their identity keys from the block of the layer's depth (`IdBlocks`, ADR 0015, seed ID
  // blocks): two layers at different depths cannot both claim an ID, and a migration that generates IDs cannot reach
  // into a layer's. This names each insert that is outside its block, for a table whose primary key is one identity column.
  public List<string> IdBlockProblems(SeedSchema schema) {
    var problems = new List<string>();
    foreach (var layer in _layers.Values.OrderBy(layer => layer.Name, StringComparer.Ordinal)) {
      var depth = ChainTo(layer.Name).Count - 1;
      foreach (var change in layer.Tables) {
        if (schema.Find(change.Table) is not { Table.PrimaryKey: [var key] } table
            || table.Columns[key].Column.GeneratedAs is not (GeneratedAs.IdentityAlways or GeneratedAs.IdentityByDefault)) {
          continue;
        }
        foreach (var id in change.Inserts.Select(row => row[key]).OfType<SeedValue.Integer>().Where(id => !IdBlocks.Contains(depth, id.Value))) {
          problems.Add($"{layer.Name}: {change.Table} inserts {key} {id.Value.ToInvariantString()}, outside the {IdBlocks.Start(depth).ToInvariantString()}..{IdBlocks.End(depth).ToInvariantString()} block of a layer at depth {depth.ToInvariantString()}.");
        }
      }
    }
    return problems;
  }

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

  // Every layer in that order, handed to `visit`, which returns what is wrong with the layer or null. A layer whose
  // parent failed (or was itself passed over for that) is not visited: it would fail the same way. Returns the
  // problems, so a caller that tries every layer reports them together.
  public async Task<List<string>> VisitParentFirstAsync(Func<SeedLayer, Task<string?>> visit) {
    var problems = new List<string>();
    var failed = new HashSet<string>(StringComparer.Ordinal);
    foreach (var layer in InBakeOrder()) {
      if (layer.Extends is not null && failed.Contains(layer.Extends)) {
        _ = failed.Add(layer.Name);
        continue;
      }
      if (await visit(layer) is { } problem) {
        _ = failed.Add(layer.Name);
        problems.Add(problem);
      }
    }
    return problems;
  }
}
