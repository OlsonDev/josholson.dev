using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Baking;

// `seeds:export`: makes a seed layer from what is in a database (ADR 0014, hierarchical seed layers), and checks it
// against the layers that are already there. `SeedTool.Exporter` hands one out, over the same templates, tree, and
// schema as the tool. Like the tool, it takes an owner connection to a database other than the one it works on,
// normally the maintenance database `postgres`.
public sealed class LayerExporter {
  private readonly SchemaTemplate _schemaTemplate;
  private readonly LayerTemplate _layerTemplate;
  private readonly ServerDatabases _databases;
  private readonly LayerTree _tree;
  private readonly SeedSchema _schema;
  private readonly IClock _clock;

  public LayerExporter(
    SchemaTemplate schemaTemplate,
    LayerTemplate layerTemplate,
    ServerDatabases databases,
    LayerTree tree,
    SeedSchema schema,
    IClock clock
  ) {
    _schemaTemplate = schemaTemplate;
    _layerTemplate = layerTemplate;
    _databases = databases;
    _tree = tree;
    _schema = schema;
    _clock = clock;
  }

  // `export`: makes a layer from what is in `database`: the difference from a baseline, `insert`s for new rows,
  // `update`s of only the columns that changed, and `delete`s. What `seed.hydration` records decides the rest:
  // - a database that was built from a layer, not for editing it, makes a child of that layer (`name` and `description`
  //   are required), the baseline being that layer's template;
  // - one built for editing a layer (`seed <layer> --edit`, or one exported before) replaces that layer in place when
  //   `name` is left out or is its name, keeping its description, and makes a sibling for any other `name`; either way
  //   the baseline is the template of the layer's parent;
  // - one that records no layer (made by the migrations alone) makes a root, from the schema template, with relative
  //   times measured from now.
  // The layer is checked before it is returned: its inserts' IDs are in its depth's block, the baseline with the layer
  // applied is exactly the database's rows, every layer that extends one it replaces still merges, and the layer and
  // those below it build in a database. Nothing is written to disk, and the database is not changed
  // (`RecordExportAsync` does that once the file is written).
  public async Task<LayerExport> ExportAsync(
    NpgsqlConnection owner,
    string database,
    string? name,
    string? description,
    bool overwrite,
    CancellationToken cancellationToken
  ) {
    if (name is not null && !LayerName.IsValid(name)) {
      throw new SeedFormatException($"'{name}' is not a layer name: use lower-kebab-case, 40 characters at most.");
    }
    if (description is not null && string.IsNullOrWhiteSpace(description)) {
      throw new SeedFormatException("A layer needs a description, a sentence about what it adds.");
    }
    DatabaseState child;
    await using (var source = await DatabaseEnvironment.OpenOwnerAsync(database, cancellationToken)) {
      child = await SeedStateReader.ReadAsync(source, _schema, cancellationToken, SeedHydrator.Anchor(_clock));
    }
    var recorded = child.Layer is null ? null : _tree.Get(child.Layer);
    var editing = child.Editing && recorded is not null;
    var replaced = editing && (name is null || string.Equals(name, recorded!.Name, StringComparison.Ordinal));
    var parent = editing ? recorded!.Extends : recorded?.Name;
    name ??= replaced ? recorded!.Name : throw new SeedFormatException("Give the new layer a --name.");
    description ??= replaced ? recorded!.Description : throw new SeedFormatException("A layer needs a description, a sentence about what it adds (--description).");
    var exists = _tree.InBakeOrder().Any(other => string.Equals(other.Name, name, StringComparison.Ordinal));
    if (exists && !replaced && !overwrite) {
      throw new SeedFormatException($"There is already a layer named '{name}'. Pass --overwrite to replace it, or pick another name.");
    }
    var export = await DiffAsync(owner, database, child, parent, name, description, exists, cancellationToken);
    var (tree, affected) = CheckAgainstTheOthers(export.Layer, replaced);
    await CheckTheyBuildAsync(owner, tree, affected, cancellationToken);
    return export;
  }

  // After the file is written: the database now stands for the layer it made, so the next export replaces that layer
  // (a database with no record gets one, with the anchor the times were measured from). The sequences need no move: the
  // rows the layer took IDs for came from its own block already.
  public async Task RecordExportAsync(string database, LayerExport export, CancellationToken cancellationToken) {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, cancellationToken);
    await SeedHydration.RecordExportAsync(connection, export.Layer.Name, export.Anchor, cancellationToken);
  }

  // The layer that is the difference between `child`'s rows and the baseline (the parent's template, or the schema
  // template for a root, which has no anchor and so is measured from the child's), checked against the baseline
  // (`LayerDiff`). `source` only names where the rows came from in a message. `seeds:check` uses it too, to see that a
  // layer's own template exports as the layer.
  internal async Task<LayerExport> DiffAsync(
    NpgsqlConnection owner,
    string source,
    DatabaseState child,
    string? parent,
    string name,
    string description,
    bool exists,
    CancellationToken cancellationToken
  ) {
    var template = parent is null
      ? (await _schemaTemplate.EnsureAsync(owner, cancellationToken)).Name
      : (await _layerTemplate.EnsureAsync(owner, parent, cancellationToken)).Name;
    var baseline = await SeedStateReader.ReadTemplateAsync(_databases, owner, template, "export", _schema, cancellationToken, child.Anchor);
    var from = parent is null ? "the schema template" : $"the template for {parent}";
    var diff = LayerDiff.Between(baseline.Rows, child.Rows, name, parent, description, _schema)
      ?? throw new SeedFormatException($"There is nothing to export: {source} holds the same rows as {from}.");
    if (!diff.GivesBackTheState) {
      throw new InvalidOperationException($"The layer does not give back the rows of {source} when it is applied to {from}. This is a bug in the export.");
    }
    return diff.ReadsBack
      ? new(diff.Layer, diff.Yaml, child.Anchor, exists)
      : throw new InvalidOperationException("The layer does not read back as the text it was written as. This is a bug in the export.");
  }

  // The tree with the new layer in place of the one of its name (if there is one) must still be sound: the layer's own
  // IDs belong to its depth's block, and every layer below it still merges and keeps to its block (a replaced layer
  // changes what they are built on, and a layer moved under another parent changes their depth). Returns that tree and the
  // names of the layer and every layer below it, the ones whose chains now differ.
  private (LayerTree Tree, List<string> Affected) CheckAgainstTheOthers(SeedLayer layer, bool replaced) {
    var others = _tree.InBakeOrder().Where(other => !string.Equals(other.Name, layer.Name, StringComparison.Ordinal));
    var tree = new LayerTree([.. others, layer]);
    var problems = new List<string>();
    var below = new Stack<SeedLayer>(tree.ChildrenOf(layer.Name));
    var names = new List<string> { layer.Name };
    while (below.TryPop(out var descendant)) {
      names.Add(descendant.Name);
      try {
        _ = LayerMerger.Merge(tree.ChainTo(descendant.Name), _schema);
      } catch (SeedFormatException exception) {
        problems.Add($"{descendant.Name} no longer applies: {exception.Message}");
      }
      foreach (var grandchild in tree.ChildrenOf(descendant.Name)) {
        below.Push(grandchild);
      }
    }
    var blocks = tree.IdBlockProblems(_schema)
      .Where(problem => names.Exists(name => problem.StartsWith($"{name}:", StringComparison.Ordinal)))
      .ToList();
    if (blocks.Count > 0) {
      var hint = replaced ? $"`seed {layer.Name} --edit`" : $"`seed {layer.Extends ?? "<layer>"}`";
      problems.AddRange(blocks);
      problems.Add($"A row made in a database built with {hint} takes its ID from the right block.");
    }
    return problems.Count > 0 ? throw new SeedFormatException(string.Join("\n", problems)) : (tree, names);
  }

  // Each affected layer's chain is applied to a clone of the schema template (`SeedHydrator.BuildProblemsAsync`), so a
  // layer that would not build is found here, naming it, instead of at the next bake.
  private async Task CheckTheyBuildAsync(NpgsqlConnection owner, LayerTree tree, List<string> affected, CancellationToken cancellationToken) {
    var problems = await SeedHydrator.BuildProblemsAsync(
      _databases,
      owner,
      (await _schemaTemplate.EnsureAsync(owner, cancellationToken)).Name,
      "check",
      tree,
      _schema,
      affected,
      SeedHydrator.Anchor(_clock),
      (name, error) => $"{name} does not build: {error}",
      cancellationToken);
    if (problems.Count > 0) {
      throw new SeedFormatException(string.Join("\n", problems));
    }
  }
}
