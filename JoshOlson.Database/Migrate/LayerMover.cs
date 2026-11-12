using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Extensions;
using Npgsql;

namespace JoshOlson.Database.Migrate;

// `seeds:migrate`'s other half: moves every seed layer from the schema it was written for to the newest one (ADR 0034,
// lock file and branch-safe migration), by running the migrations over the layer's own rows. The layers in the working
// tree are the input, read against the old lock's schema.
//
// 1. Each layer, parent first, goes into a clone of the template at the locked version (its chain applied with foreign
//    keys not enforced and the sequences positioned by the root, `SeedHydrator.HydrateForMigrationAsync`).
// 2. The migrations after that version run on the clone, repeatable ones included (`SchemaTemplate.MigrateToNewestAsync`).
//    The database is then the layer's full state at the newest version, which `SeedStateReader` reads as the new schema.
// 3. Each layer is exported again as the difference between its state and its parent's (a root's: the newest template's),
//    which is how a migration that adds a `NOT NULL` column puts it in every insert, and one that changes data changes it
//    in the layer.
// 4. Each new layer applied to its parent's state gives back its own, its inserts keep to their ID blocks, its text reads
//    back as itself, and it builds into a database at the newest version, where foreign keys are enforced and reference
//    data is all there.
//
// Everything is checked before anything is written, every problem is collected, and a layer whose parent failed is not
// tried. The caller writes the files and the lock, and only when this returns.
//
// A migration has to give the same result for the same rows every time: each layer is migrated in a database of its own,
// and a child differs from its parent only by what the child's layer says. A default of `now()` on a new column would make
// a child seem to update every row it inherits.
public static class LayerMover {
  public static async Task<List<MovedLayer>> MoveAsync(
    NpgsqlConnection owner,
    MigrateOptions options,
    SchemaLock existing,
    SchemaLock updated,
    List<MigrationFile> onDisk,
    LayerTree oldTree,
    Dictionary<string, string> authored,
    HashSet<string> dropped,
    CancellationToken cancellationToken
  ) {
    var oldSchema = new SeedSchema(existing);
    var newSchema = new SeedSchema(updated);
    var anchor = SeedHydrator.Anchor(options.Clock);

    var (fromTemplate, _) = await options.Template.EnsureAsync(owner, existing.Version, cancellationToken);
    var skipped = await options.Template.SkippedAsync(owner, fromTemplate, cancellationToken);
    var (headTemplate, _) = await options.Template.EnsureAsync(owner, cancellationToken);

    var problems = new List<string>();
    var states = new Dictionary<string, SeedState>(StringComparer.Ordinal);
    foreach (var layer in oldTree.InBakeOrder()) {
      if (layer.Extends is not null && !states.ContainsKey(layer.Extends)) {
        continue;
      }
      try {
        states[layer.Name] = await MigrateLayerAsync(owner, options, fromTemplate, oldTree, oldSchema, newSchema, layer.Name, onDisk, anchor, cancellationToken);
      } catch (SeedFormatException exception) {
        problems.Add($"{layer.Name}: {exception.Message}");
      }
    }
    var baseline = await ReadHeadAsync(owner, options, headTemplate, newSchema, anchor, cancellationToken);

    var moved = new List<MovedLayer>();
    if (problems.Count is 0) {
      moved = Rewrite(oldTree, states, baseline, newSchema, problems);
    }
    if (problems.Count is 0) {
      moved = Reconcile(moved, authored, dropped, newSchema, problems);
    }
    if (problems.Count is 0) {
      problems.AddRange(await BuildAtHeadAsync(owner, options, headTemplate, moved, newSchema, anchor, cancellationToken));
    }
    if (problems.Count > 0) {
      if (skipped.Count > 0) {
        problems.Add(
          $"Without {string.Join(", ", skipped.Select(repeatable => repeatable.File))} (which cannot apply at version {existing.Version.ToInvariantString()}) "
          + "a row may have pointed at reference data that was not there yet, which is not checked until the newest version.");
      }
      throw new SeedFormatException(string.Join("\n", problems));
    }
    return moved;
  }

  // One layer's full state at the newest schema: its chain put into a database at the old one and migrated on.
  private static async Task<SeedState> MigrateLayerAsync(
    NpgsqlConnection owner,
    MigrateOptions options,
    string fromTemplate,
    LayerTree oldTree,
    SeedSchema oldSchema,
    SeedSchema newSchema,
    string layer,
    List<MigrationFile> onDisk,
    NodaTime.Instant anchor,
    CancellationToken cancellationToken
  ) {
    var database = $"{SeedDatabaseNames.Prefix}move_{Guid.NewGuid():N}";
    await options.Databases.CloneAsync(owner, fromTemplate, database, cancellationToken);
    try {
      _ = await SeedHydrator.HydrateForMigrationAsync(database, oldTree, oldSchema, layer, anchor, cancellationToken);
      try {
        await options.Template.MigrateToNewestAsync(database, cancellationToken);
      } catch (InvalidOperationException exception) {
        throw new SeedFormatException($"the migrations failed on its rows: {SchemaTemplate.FirstError(exception.Message)}", exception);
      }
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(database));
      await connection.OpenAsync(cancellationToken);
      var history = MigrationHistory.Check(onDisk, await MigrationHistory.ReadAsync(connection, cancellationToken));
      return history.Count is 0
        ? (await SeedStateReader.ReadAsync(connection, newSchema, cancellationToken)).Rows
        : throw new SeedFormatException(string.Join(" ", history));
    } finally {
      await options.Databases.DropAsync(owner, database, CancellationToken.None);
    }
  }

  // The rows of the newest template, which is what a root layer's state is measured from: it holds the reference data
  // every database has.
  private static async Task<SeedState> ReadHeadAsync(
    NpgsqlConnection owner,
    MigrateOptions options,
    string headTemplate,
    SeedSchema newSchema,
    NodaTime.Instant anchor,
    CancellationToken cancellationToken
  ) {
    var database = $"{SeedDatabaseNames.Prefix}move_{Guid.NewGuid():N}";
    await options.Databases.CloneAsync(owner, headTemplate, database, cancellationToken);
    try {
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(database));
      await connection.OpenAsync(cancellationToken);
      return (await SeedStateReader.ReadAsync(connection, newSchema, cancellationToken, anchor)).Rows;
    } finally {
      await options.Databases.DropAsync(owner, database, CancellationToken.None);
    }
  }

  // Each layer again, as the difference between its migrated state and its parent's, and checked in memory: the parent's
  // rows with the layer applied are the layer's state, its inserts' IDs are in their block, and its text reads back as the
  // same layer.
  private static List<MovedLayer> Rewrite(
    LayerTree oldTree,
    Dictionary<string, SeedState> states,
    SeedState baseline,
    SeedSchema newSchema,
    List<string> problems
  ) {
    var layers = new List<SeedLayer>();
    foreach (var layer in oldTree.InBakeOrder()) {
      var parent = layer.Extends is null ? baseline : states[layer.Extends];
      var tables = StateDiffer.Diff(parent, states[layer.Name], newSchema);
      if (tables.Count is 0) {
        problems.Add($"{layer.Name}: nothing is left for it to add after the migrations.");
        continue;
      }
      layers.Add(new() {
        Name = layer.Name,
        Extends = layer.Extends,
        Description = layer.Description,
        Tables = tables,
      });
    }
    if (problems.Count > 0) {
      return [];
    }
    var tree = new LayerTree(layers);
    problems.AddRange(tree.IdBlockProblems(newSchema));
    var moved = new List<MovedLayer>(layers.Count);
    foreach (var layer in layers) {
      var rebuilt = LayerMerger.Merge([StateLayer.Of(baseline, "baseline"), .. tree.ChainTo(layer.Name)], newSchema);
      if (!SeedValues.SameState(rebuilt, states[layer.Name])) {
        problems.Add($"{layer.Name}: the layer does not give back its migrated rows when it is applied to its parent's. This is a bug in the migration.");
      }
      var yaml = LayerWriter.Write(layer, newSchema);
      if (!string.Equals(LayerWriter.Write(LayerParser.Parse(layer.Name, yaml, newSchema), newSchema), yaml, StringComparison.Ordinal)) {
        problems.Add($"{layer.Name}: the layer does not read back as the text it was written as. This is a bug in the migration.");
      }
      moved.Add(new(layer, yaml));
    }
    return moved;
  }

  // The layers as they will be after the run: the ones the tool made, except those somebody has their own file for, which are
  // kept exactly as written and read against the newest schema (a table or column a migration renamed since is a problem to
  // fix by hand, never something to guess at), and without the ones the branch deleted, which were only needed as parents
  // to move the rest. The whole set must still be a sound tree.
  private static List<MovedLayer> Reconcile(
    List<MovedLayer> moved,
    Dictionary<string, string> authored,
    HashSet<string> dropped,
    SeedSchema newSchema,
    List<string> problems
  ) {
    var all = moved.FindAll(layer => !dropped.Contains(layer.Layer.Name) && !authored.ContainsKey(layer.Layer.Name));
    foreach (var (name, text) in authored.OrderBy(entry => entry.Key, StringComparer.Ordinal)) {
      try {
        all.Add(new(LayerParser.Parse(name, text, newSchema), text, Authored: true));
      } catch (SeedFormatException exception) {
        problems.Add(
          $"{name}: {exception.Message}\nIt is your own file, so it was not changed. Edit it to the new names, or make it again: `seed <its parent>`, put the data back, `seeds:export`.");
      }
    }
    if (problems.Count > 0) {
      return [];
    }
    try {
      problems.AddRange(new LayerTree([.. all.ConvertAll(layer => layer.Layer)]).IdBlockProblems(newSchema));
    } catch (SeedFormatException exception) {
      problems.Add(exception.Message);
    }
    return all;
  }

  // Each new layer's chain, applied to a database at the newest version, which enforces what the in-memory merge cannot:
  // a foreign key, a unique value, a check. The reference data from the repeatable migrations is there by now.
  private static async Task<List<string>> BuildAtHeadAsync(
    NpgsqlConnection owner,
    MigrateOptions options,
    string headTemplate,
    List<MovedLayer> moved,
    SeedSchema newSchema,
    NodaTime.Instant anchor,
    CancellationToken cancellationToken
  ) {
    var tree = new LayerTree([.. moved.ConvertAll(layer => layer.Layer)]);
    var problems = new List<string>();
    foreach (var layer in moved) {
      var database = $"{SeedDatabaseNames.Prefix}move_{Guid.NewGuid():N}";
      await options.Databases.CloneAsync(owner, headTemplate, database, cancellationToken);
      try {
        _ = await SeedHydrator.HydrateAsync(database, tree, newSchema, layer.Layer.Name, editing: false, anchor, cancellationToken);
      } catch (SeedFormatException exception) {
        problems.Add($"{layer.Layer.Name}: does not build at the newest version: {exception.Message}");
      } finally {
        await options.Databases.DropAsync(owner, database, CancellationToken.None);
      }
    }
    return problems;
  }
}
