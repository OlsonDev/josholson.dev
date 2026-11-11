using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Data;
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
  private readonly DatabaseCloner _cloner;
  private readonly SeedSchema _schema;
  private readonly IClock _clock;

  private SeedTool(
    SchemaTemplate schemaTemplate,
    LayerTemplate layerTemplate,
    DatabaseCloner cloner,
    LayerTree tree,
    SeedSchema schema,
    IClock clock,
    string development
  ) {
    _schemaTemplate = schemaTemplate;
    _layerTemplate = layerTemplate;
    _cloner = cloner;
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
    => new(_schemaTemplate, _layerTemplate, _cloner, Tree, _schema, _clock, development);

  // Reads the lock and the layers under the repository root and sets everything up. The lock has to exist: it types
  // every value in a layer.
  public static async Task<SeedTool> OpenAsync(SeedPaths paths, IClock clock, CancellationToken cancellationToken) {
    if (!File.Exists(paths.LockFile)) {
      throw new SeedFormatException($"{paths.LockFile} does not exist. Run mise run seeds:migrate first.");
    }
    var schemaLock = SchemaLockParser.Parse(await File.ReadAllTextAsync(paths.LockFile, cancellationToken));
    return await OpenAsync(paths, new SeedSchema(schemaLock), clock, cancellationToken);
  }

  // The same, for a schema the caller already has: `seeds:migrate` checks the layers against the lock it is about to
  // write, which is not on disk yet.
  public static async Task<SeedTool> OpenAsync(SeedPaths paths, SeedSchema schema, IClock clock, CancellationToken cancellationToken) {
    var tree = await LayerLoader.LoadAsync(paths.Seeds, schema, cancellationToken);
    var cloner = new DatabaseCloner();
    var tools = new ComposeTools(paths.Root);
    var schemaTemplate = new SchemaTemplate(SchemaTemplateOptions.ForRepository(paths), tools);
    var layerTemplate = new LayerTemplate(schemaTemplate, cloner, tree, schema, paths.Seeds, clock);
    return new(schemaTemplate, layerTemplate, cloner, tree, schema, clock, DatabaseEnvironment.DevelopmentDatabase());
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
  public async Task<List<string>> CheckAsync(NpgsqlConnection owner, CancellationToken cancellationToken) {
    var problems = new List<string>();
    var failed = new HashSet<string>(StringComparer.Ordinal);
    foreach (var layer in Tree.InBakeOrder()) {
      if (layer.Extends is not null && failed.Contains(layer.Extends)) {
        _ = failed.Add(layer.Name);
        continue;
      }
      try {
        _ = await _layerTemplate.EnsureAsync(owner, layer.Name, cancellationToken);
      } catch (SeedFormatException exception) {
        _ = failed.Add(layer.Name);
        problems.Add($"{layer.Name}: {exception.Message}");
      }
    }
    return problems;
  }

  // `seed:test <id> <layer>`: a database of its own for one test, cloned from the layer's template. Returns its name.
  public async Task<string> CreateTestDatabaseAsync(NpgsqlConnection owner, string id, string layer, CancellationToken cancellationToken) {
    var name = SeedDatabaseNames.Test(id);
    _ = Tree.Get(layer);
    if (await SchemaTemplate.ExistsAsync(owner, name, cancellationToken)) {
      throw new SeedFormatException($"{name} already exists. Drop it with seed:clean, or pick another id.");
    }
    var (template, _) = await _layerTemplate.EnsureAsync(owner, layer, cancellationToken);
    await _cloner.CloneAsync(owner, template, name, cancellationToken);
    return name;
  }

  // `seed <layer>`: drops the named development database and makes it again from the schema template with the layer's
  // rows in it, their relative times measured from now (a clone of the layer's template would keep the time it was
  // baked). Everything in that database is lost. `Development` is its name, POSTGRES_DB unless `WithDevelopment` says
  // otherwise.
  public async Task<int> SeedDevelopmentAsync(NpgsqlConnection owner, string layer, CancellationToken cancellationToken) {
    var quoted = SeedDatabaseNames.QuoteDevelopment(Development);
    _ = Tree.Get(layer);
    var (schemaName, _) = await _schemaTemplate.EnsureAsync(owner, cancellationToken);
    await owner.ExecuteAsync($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE);", null, cancellationToken);
    await _cloner.CloneDevelopmentAsync(owner, schemaName, Development, cancellationToken);
    return await SeedHydrator.HydrateAsync(Development, Tree, _schema, layer, SeedHydrator.Anchor(_clock), cancellationToken);
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
    _ = SeedDatabaseNames.Quote(prefix);
    var keep = new HashSet<string>(StringComparer.Ordinal);
    if (!everything) {
      _ = keep.Add(await _schemaTemplate.NameAsync(cancellationToken));
      foreach (var layer in Tree.InBakeOrder()) {
        _ = keep.Add(await _layerTemplate.NameAsync(layer.Name, cancellationToken));
      }
    }
    var names = await owner.QueryAsync<string>(
      "SELECT datname FROM pg_database WHERE starts_with(datname, @prefix) ORDER BY datname;",
      new { prefix },
      cancellationToken);
    var dropped = new List<string>(names.Count);
    foreach (var name in names.Where(name => !keep.Contains(name))) {
      await SchemaTemplate.DropAsync(owner, name, cancellationToken);
      dropped.Add(name);
    }
    return dropped;
  }
}
