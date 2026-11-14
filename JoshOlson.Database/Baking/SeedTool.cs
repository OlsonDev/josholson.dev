using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Extensions;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Baking;

// What the commands `seeds:bake`, `seed`, `seed:test`, and `seed:clean` do (ADR 0033, a scoped database per test),
// over the repository's own migrations, provisioning, and seeds. Each takes an owner connection to a database other
// than the one it rebuilds, normally the maintenance database `postgres`: Postgres cannot drop or clone a database
// somebody is connected to.
public sealed class SeedTool {
  private readonly SchemaTemplate _schemaTemplate;
  private readonly LayerTemplate _layerTemplate;
  private readonly ServerDatabases _databases;
  private readonly SeedSchema _schema;
  private readonly IClock _clock;

  private SeedTool(
    SchemaTemplate schemaTemplate,
    LayerTemplate layerTemplate,
    ServerDatabases databases,
    LayerTree tree,
    SeedSchema schema,
    IClock clock,
    string development
  ) {
    _schemaTemplate = schemaTemplate;
    _layerTemplate = layerTemplate;
    _databases = databases;
    Tree = tree;
    _schema = schema;
    _clock = clock;
    Development = development;
  }

  public LayerTree Tree { get; }

  // The database `seed <layer>` drops and makes again: POSTGRES_DB.
  public string Development { get; }

  // The same tool for another development database, which is how a test rebuilds one of its own.
  public SeedTool WithDevelopment(string development)
    => new(_schemaTemplate, _layerTemplate, _databases, Tree, _schema, _clock, development);

  // Reads the lock and the layers under the repository root and sets everything up, with the database operations and the
  // container tools the caller has, which is how the command line shares one of each across a run. The lock has to
  // exist: it types every value in a layer.
  public static async Task<SeedTool> OpenAsync(
    SeedPaths paths,
    ServerDatabases databases,
    ComposeTools tools,
    IClock clock,
    CancellationToken cancellationToken
  )
    => await OpenAsync(paths, databases, tools, await SeedSchema.LoadAsync(paths, cancellationToken), clock, cancellationToken);

  // The same, for a schema the caller already has: `seeds:migrate` checks the layers against the lock it is about to
  // write, which is not on disk yet.
  public static async Task<SeedTool> OpenAsync(
    SeedPaths paths,
    ServerDatabases databases,
    ComposeTools tools,
    SeedSchema schema,
    IClock clock,
    CancellationToken cancellationToken
  ) {
    var tree = await LayerLoader.LoadAsync(paths.Seeds, schema, cancellationToken);
    var schemaTemplate = new SchemaTemplate(SchemaTemplateOptions.ForRepository(paths), tools, databases);
    var layerTemplate = new LayerTemplate(schemaTemplate, databases, tree, schema, paths.Seeds, clock);
    return new(schemaTemplate, layerTemplate, databases, tree, schema, clock, DatabaseEnvironment.DevelopmentDatabase());
  }

  // Builds the schema template and then every layer's template, parent first. Templates that exist for the current
  // files are left alone. Returns the layers in the order built, each with its template's name and whether this run
  // built it.
  public async Task<List<(string Layer, string Name, bool Built)>> BakeAsync(NpgsqlConnection owner, CancellationToken cancellationToken) {
    var (schemaName, schemaBuilt) = await _schemaTemplate.EnsureAsync(owner, cancellationToken);
    var baked = new List<(string Layer, string Name, bool Built)> { ("(schema)", schemaName, schemaBuilt) };
    foreach (var layer in Tree.InBakeOrder()) {
      var (name, built) = await _layerTemplate.EnsureAsync(owner, layer.Name, cancellationToken);
      baked.Add((layer.Name, name, built));
    }
    return baked;
  }

  // What `seeds:migrate` asks of the layers: each has to build into a template from the migrations on disk. Every layer
  // is tried, so the problems come back together, but one whose parent failed is skipped (it would fail the same way).
  // A failed build leaves nothing behind. Returns the problems, empty when all the layers apply.
  public async Task<List<string>> CheckAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    => await Tree.VisitParentFirstAsync(async layer => {
      try {
        _ = await _layerTemplate.EnsureAsync(owner, layer.Name, cancellationToken);
        return null;
      } catch (SeedFormatException exception) {
        return $"{layer.Name}: {exception.Message}";
      }
    });

  // `export`: makes a layer from what is in `database` (ADR 0014, hierarchical seed layers): the difference from a
  // baseline, `insert`s for new rows, `update`s of only the columns that changed, and `delete`s. What `seed.hydration`
  // records decides the rest:
  // - a database that was built from a layer, not for editing it, makes a child of that layer (`name` and `description`
  //   are required), the baseline being that layer's template;
  // - one built for editing a layer (`seed <layer> --edit`, or one exported before) replaces that layer in place when
  //   `name` is left out or is its name, keeping its description, and makes a sibling for any other `name`; either way
  //   the baseline is the template of the layer's parent;
  // - one that records no layer (made by the migrations alone) makes a root, from the schema template, with relative
  //   times measured from now.
  // The layer is checked before it is returned: its inserts' IDs are in its depth's block, the baseline with the layer
  // applied is exactly the database's rows, every layer that extends one it replaces still merges, and the layer and those below it build
  // in a database. A template refuses connections, so a clone of the baseline's is read and dropped. Nothing is written to disk, and the database
  // is not changed (`RecordExportAsync` does that once the file is written).
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
    var recorded = child.Layer is null ? null : Tree.Get(child.Layer);
    var editing = child.Editing && recorded is not null;
    var replaced = editing && (name is null || string.Equals(name, recorded!.Name, StringComparison.Ordinal));
    var parent = editing ? recorded!.Extends : recorded?.Name;
    name ??= replaced ? recorded!.Name : throw new SeedFormatException("Give the new layer a --name.");
    description ??= replaced ? recorded!.Description : throw new SeedFormatException("A layer needs a description, a sentence about what it adds (--description).");
    var exists = Tree.InBakeOrder().Any(other => string.Equals(other.Name, name, StringComparison.Ordinal));
    if (exists && !replaced && !overwrite) {
      throw new SeedFormatException($"There is already a layer named '{name}'. Pass --overwrite to replace it, or pick another name.");
    }
    var export = await DiffAsync(owner, database, child, parent, name, description, exists, cancellationToken);
    var (tree, affected) = CheckAgainstTheOthers(export.Layer, replaced);
    await CheckTheyBuildAsync(owner, tree, affected, cancellationToken);
    return export;
  }

  // The layer that is the difference between `child`'s rows and the baseline (the parent's template, or the schema
  // template for a root), checked against the baseline: the baseline with the layer applied is exactly the rows, and the
  // text of the layer reads back as the same layer. `database` only names the source in a message.
  private async Task<LayerExport> DiffAsync(
    NpgsqlConnection owner,
    string database,
    DatabaseState child,
    string? parent,
    string name,
    string description,
    bool exists,
    CancellationToken cancellationToken
  ) {
    var baseline = await ReadBaselineAsync(owner, parent, child.Anchor, cancellationToken);
    var from = parent is null ? "the schema template" : $"the template for {parent}";
    var diff = LayerDiff.Between(baseline, child.Rows, name, parent, description, _schema)
      ?? throw new SeedFormatException($"There is nothing to export: {database} holds the same rows as {from}.");
    if (!diff.GivesBackTheState) {
      throw new InvalidOperationException($"The layer does not give back the rows of {database} when it is applied to {from}. This is a bug in the export.");
    }
    return diff.ReadsBack
      ? new(diff.Layer, diff.Yaml, child.Anchor, exists)
      : throw new InvalidOperationException("The layer does not read back as the text it was written as. This is a bug in the export.");
  }

  // `seeds:check`, the layers' half: every layer must be in the canonical form `LayerWriter` writes (so the diff of a
  // change is only the change), build into a template, and give itself back: the rows of a database built from it, read
  // and diffed against its parent's template, are exactly the layer. Every layer is tried, so the problems come back
  // together, but one whose parent failed is skipped (it would fail the same way). Returns the problems, empty when
  // all the layers pass.
  public async Task<List<string>> CheckFilesAsync(NpgsqlConnection owner, string seedsDirectory, CancellationToken cancellationToken)
    => await Tree.VisitParentFirstAsync(layer => CheckFileAsync(owner, seedsDirectory, layer, cancellationToken));

  private async Task<string?> CheckFileAsync(NpgsqlConnection owner, string seedsDirectory, SeedLayer layer, CancellationToken cancellationToken) {
    var canonical = LayerWriter.Write(layer, _schema);
    var path = LayerLoader.PathOf(seedsDirectory, layer.Name);
    var onDisk = await File.ReadAllTextAsync(path, cancellationToken);
    if (!string.Equals(onDisk, canonical, StringComparison.Ordinal)) {
      return $"{layer.Name}: {path} is not in canonical form ({FirstDifference(canonical, onDisk)}).";
    }
    try {
      var (template, _) = await _layerTemplate.EnsureAsync(owner, layer.Name, cancellationToken);
      await using var scratch = await _databases.CloneScratchAsync(owner, template, "check", cancellationToken);
      DatabaseState state;
      await using (var connection = await scratch.OpenAsync(cancellationToken)) {
        state = await SeedStateReader.ReadAsync(connection, _schema, cancellationToken);
      }
      var again = await DiffAsync(owner, scratch.Name, state, layer.Extends, layer.Name, layer.Description, exists: true, cancellationToken);
      return string.Equals(again.Yaml, canonical, StringComparison.Ordinal)
        ? null
        : $"{layer.Name}: a database built from it exports as something else ({FirstDifference(canonical, again.Yaml)}).";
    } catch (SeedFormatException exception) {
      return $"{layer.Name}: {exception.Message}";
    }
  }

  // Where two texts first part ways, for a message: the line number and both lines.
  private static string FirstDifference(string expected, string found) {
    var expectedLines = expected.Split('\n');
    var foundLines = found.Split('\n');
    for (var line = 0; line < Math.Max(expectedLines.Length, foundLines.Length); line++) {
      var left = line < expectedLines.Length ? expectedLines[line] : "(end of file)";
      var right = line < foundLines.Length ? foundLines[line] : "(end of file)";
      if (!string.Equals(left, right, StringComparison.Ordinal)) {
        return $"line {(line + 1).ToInvariantString()}: expected `{left}`, found `{right}`";
      }
    }
    return "no line differs";
  }

  // After the file is written: the database now stands for the layer it made, so the next export replaces that layer
  // (a database with no record gets one, with the anchor the times were measured from). The sequences need no move: the
  // rows the layer took IDs for came from its own block already.
  public async Task RecordExportAsync(string database, LayerExport export, CancellationToken cancellationToken) {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, cancellationToken);
    await SeedHydration.RecordExportAsync(connection, export.Layer.Name, export.Anchor, cancellationToken);
  }

  // The tree with the new layer in place of the one of its name (if there is one) must still be sound: the layer's own
  // IDs belong to its depth's block, and every layer below it still merges and keeps to its block (a replaced layer
  // changes what they are built on, and a layer moved under another parent changes their depth). Returns that tree and the
  // names of the layer and every layer below it, the ones whose chains now differ.
  private (LayerTree Tree, List<string> Affected) CheckAgainstTheOthers(SeedLayer layer, bool replaced) {
    var others = Tree.InBakeOrder().Where(other => !string.Equals(other.Name, layer.Name, StringComparison.Ordinal));
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

  // The rows the export is measured from: the parent's template, or the schema template for a root. The schema template
  // has no anchor, so a root's baseline is measured from the database's own.
  private async Task<SeedState> ReadBaselineAsync(NpgsqlConnection owner, string? parent, Instant anchor, CancellationToken cancellationToken) {
    var template = parent is null
      ? (await _schemaTemplate.EnsureAsync(owner, cancellationToken)).Name
      : (await _layerTemplate.EnsureAsync(owner, parent, cancellationToken)).Name;
    var baseline = await SeedStateReader.ReadTemplateAsync(_databases, owner, template, "export", _schema, cancellationToken, anchor);
    return baseline.Rows;
  }

  // `seed:test <id> <layer>`: a database of its own for one test, cloned from the layer's template. Returns its name.
  public async Task<string> CreateTestDatabaseAsync(NpgsqlConnection owner, string id, string layer, CancellationToken cancellationToken) {
    var name = SeedDatabaseNames.Test(id);
    _ = Tree.Get(layer);
    if (await _databases.ExistsAsync(owner, name, cancellationToken)) {
      throw new SeedFormatException($"{name} already exists. Drop it with seed:clean, or pick another id.");
    }
    var (template, _) = await _layerTemplate.EnsureAsync(owner, layer, cancellationToken);
    await _databases.CloneAsync(owner, template, name, cancellationToken);
    return name;
  }

  // `seed <layer>`: drops the named development database and makes it again from the schema template with the layer's
  // rows in it, their relative times measured from now (a clone of the layer's template would keep the time it was
  // baked). Everything in that database is lost. `Development` is its name, POSTGRES_DB unless `WithDevelopment` says
  // otherwise. `editing` is for changing the layer itself: the sequences stay in the layer's own block, so what is made
  // exports back into it, where a database made for a child of the layer (not editing) hands out the next block's.
  public async Task<int> SeedDevelopmentAsync(NpgsqlConnection owner, string layer, bool editing, CancellationToken cancellationToken) {
    _ = SeedDatabaseNames.QuoteDevelopment(Development);
    _ = Tree.Get(layer);
    var (schemaName, _) = await _schemaTemplate.EnsureAsync(owner, cancellationToken);
    await _databases.DropDevelopmentAsync(owner, Development, cancellationToken);
    await _databases.CloneDevelopmentAsync(owner, schemaName, Development, cancellationToken);
    return await SeedHydrator.HydrateAsync(Development, Tree, _schema, layer, editing, SeedHydrator.Anchor(_clock), cancellationToken);
  }

  // `seed:clean`: drops every seed tool database except the templates the current files name (all of them with
  // `everything`): older templates, test databases, and builds that died halfway. Returns the names dropped. Only
  // names that start with `prefix` are considered, `seed_` for the command, so a test can work on a prefix of its own.
  public async Task<List<string>> CleanAsync(
    NpgsqlConnection owner,
    bool everything,
    string prefix,
    CancellationToken cancellationToken
  ) {
    var keep = new HashSet<string>(StringComparer.Ordinal);
    if (!everything) {
      _ = keep.Add(await _schemaTemplate.NameAsync(cancellationToken));
      foreach (var layer in Tree.InBakeOrder()) {
        _ = keep.Add(await _layerTemplate.NameAsync(layer.Name, cancellationToken));
      }
    }
    var names = await _databases.ListAsync(owner, prefix, cancellationToken);
    var dropped = new List<string>(names.Count);
    foreach (var name in names.Where(name => !keep.Contains(name))) {
      await _databases.DropAsync(owner, name, cancellationToken);
      dropped.Add(name);
    }
    return dropped;
  }
}
