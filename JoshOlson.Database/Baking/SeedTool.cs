using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Extensions;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Baking;

// What the commands `seeds:bake`, `seeds:check`, `seed`, `seed:test`, and `seed:clean` do (ADR 0033, a scoped database
// per test), over the repository's own migrations, provisioning, and seeds; `seeds:export` is `Exporter`. Each takes an
// owner connection to a database other than the one it rebuilds, normally the maintenance database `postgres`:
// Postgres cannot drop or clone a database somebody is connected to.
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
    Exporter = new(schemaTemplate, layerTemplate, databases, tree, schema, clock);
  }

  public LayerTree Tree { get; }

  // The database `seed <layer>` drops and makes again: POSTGRES_DB.
  public string Development { get; }

  // `seeds:export`, over the same templates, layers, and schema.
  public LayerExporter Exporter { get; }

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
      var state = await SeedStateReader.ReadTemplateAsync(_databases, owner, template, "check", _schema, cancellationToken);
      var again = await Exporter.DiffAsync(owner, template, state, layer.Extends, layer.Name, layer.Description, exists: true, cancellationToken);
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
