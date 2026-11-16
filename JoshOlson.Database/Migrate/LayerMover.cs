using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Extensions;
using JoshOlson.Shared.Results;
using NodaTime;
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
//
// A mover is made for one move and holds what every step of it needs: the owner connection, the options, the move, the
// new schema, the anchor, and the problems found so far.
public sealed class LayerMover {
  private readonly NpgsqlConnection _owner;
  private readonly MigrateOptions _options;
  private readonly LayerMove _move;
  private readonly CancellationToken _cancellationToken;
  private readonly SeedSchema _newSchema;
  private readonly Instant _anchor;
  private readonly SeedProblems _problems = new();

  public LayerMover(NpgsqlConnection owner, MigrateOptions options, LayerMove move, CancellationToken cancellationToken) {
    _owner = owner;
    _options = options;
    _move = move;
    _cancellationToken = cancellationToken;
    _newSchema = new(move.To);
    _anchor = SeedHydrator.Anchor(options.Clock);
  }

  // The layers at the newest schema, or every problem that kept one of them from getting there.
  public async Task<Result<List<MovedLayer>, SeedProblems>> MoveAsync() {
    var (fromTemplate, _) = await _options.Template.EnsureAsync(_owner, _move.From.Version, _cancellationToken);
    var skipped = await _options.Template.SkippedAsync(_owner, fromTemplate, _cancellationToken);
    var (headTemplate, _) = await _options.Template.EnsureAsync(_owner, _cancellationToken);

    var states = new Dictionary<string, SeedState>(StringComparer.Ordinal);
    _problems.AddRange(await _move.OldTree.VisitParentFirstAsync(async layer => {
      var migrated = await MigrateLayerAsync(fromTemplate, layer.Name);
      if (migrated is SeedProblems unmoved) {
        return $"{layer.Name}: {unmoved}";
      }
      states[layer.Name] = migrated.OrThrow();
      return null;
    }));
    // The rows of the newest template, which is what a root layer's state is measured from: it holds the reference data
    // every database has.
    var head = await SeedStateReader.ReadTemplateAsync(_options.Databases, _owner, headTemplate, "move", _newSchema, _cancellationToken, _anchor);

    var moved = new List<MovedLayer>();
    if (!_problems.Any) {
      moved = Rewrite(states, head.Rows);
    }
    if (!_problems.Any) {
      moved = Reconcile(moved);
    }
    if (!_problems.Any) {
      // Each new layer's chain, applied to a database at the newest version, which enforces what the in-memory merge
      // cannot. The reference data from the repeatable migrations is there by now.
      _problems.AddRange(await SeedHydrator.BuildProblemsAsync(
        _options.Databases,
        _owner,
        headTemplate,
        "move",
        new LayerTree(_newSchema, [.. moved.ConvertAll(layer => layer.Layer)]),
        moved.ConvertAll(layer => layer.Layer.Name),
        _anchor,
        (name, error) => $"{name}: does not build at the newest version: {error}",
        _cancellationToken));
    }
    if (_problems.Any && skipped.Count > 0) {
      _problems.Add(
        $"Without {string.Join(", ", skipped.Select(repeatable => repeatable.File))} (which cannot apply at version {_move.From.Version.ToInvariantString()}) "
        + "a row may have pointed at reference data that was not there yet, which is not checked until the newest version.");
    }
    return _problems.Any ? _problems : moved;
  }

  // One layer's full state at the newest schema: its chain put into a database at the old one and migrated on.
  private async Task<Result<SeedState, SeedProblems>> MigrateLayerAsync(string fromTemplate, string layer) {
    await using var scratch = await _options.Databases.CloneScratchAsync(_owner, fromTemplate, "move", _cancellationToken);
    if (await SeedHydrator.HydrateForMigrationAsync(scratch.Name, _move.OldTree, layer, _anchor, _cancellationToken) is SeedProblems refused) {
      return refused;
    }
    try {
      await _options.Template.MigrateToNewestAsync(scratch.Name, _cancellationToken);
    } catch (InvalidOperationException exception) {
      return SeedProblems.Of($"the migrations failed on its rows: {SchemaTemplate.FirstError(exception.Message)}");
    }
    await using var connection = await scratch.OpenAsync(_cancellationToken);
    var history = MigrationHistory.Check(_move.OnDisk, await MigrationHistory.ReadAsync(connection, _cancellationToken));
    return history.Count is 0
      ? (await SeedStateReader.ReadAsync(connection, _newSchema, _cancellationToken)).Rows
      : SeedProblems.Of(string.Join(" ", history));
  }

  // Each layer again, as the difference between its migrated state and its parent's, and checked in memory: the parent's
  // rows with the layer applied are the layer's state, its inserts' IDs are in their block, and its text reads back as the
  // same layer.
  private List<MovedLayer> Rewrite(Dictionary<string, SeedState> states, SeedState baseline) {
    var diffs = new List<LayerDiff>();
    foreach (var layer in _move.OldTree.InBakeOrder()) {
      var parent = layer.Extends is null ? baseline : states[layer.Extends];
      var diff = LayerDiff.Between(parent, states[layer.Name], layer.Header, _newSchema);
      if (diff is null) {
        _problems.Add($"{layer.Name}: nothing is left for it to add after the migrations.");
        continue;
      }
      diffs.Add(diff);
    }
    if (_problems.Any) {
      return [];
    }
    _problems.AddRange(new LayerTree(_newSchema, diffs.ConvertAll(diff => diff.Layer)).IdBlockProblems());
    var moved = new List<MovedLayer>(diffs.Count);
    foreach (var diff in diffs) {
      var name = diff.Layer.Name;
      if (!diff.GivesBackTheState) {
        _problems.Add($"{name}: the layer does not give back its migrated rows when it is applied to its parent's. This is a bug in the migration.");
      }
      if (!diff.ReadsBack) {
        _problems.Add($"{name}: the layer does not read back as the text it was written as. This is a bug in the migration.");
      }
      moved.Add(new(diff.Layer, diff.Yaml));
    }
    return moved;
  }

  // The layers as they will be after the run: the ones the tool made, except those somebody has their own file for, which are
  // kept exactly as written and read against the newest schema (a table or column a migration renamed since is a problem to
  // fix by hand, never something to guess at), and without the ones the branch deleted, which were only needed as parents
  // to move the rest. The whole set must still be a sound tree.
  private List<MovedLayer> Reconcile(List<MovedLayer> moved) {
    var all = moved.FindAll(layer => !_move.Dropped.Contains(layer.Layer.Name) && !_move.Authored.ContainsKey(layer.Layer.Name));
    foreach (var (name, text) in _move.Authored.OrderBy(entry => entry.Key, StringComparer.Ordinal)) {
      var parsed = LayerParser.Parse(name, text, _newSchema);
      if (parsed is SeedProblems unread) {
        _problems.Add(
          $"{name}: {unread}\nIt is your own file, so it was not changed. Edit it to the new names, or make it again: `seed <its parent>`, put the data back, `seeds:export`.");
        continue;
      }
      all.Add(new(parsed.OrThrow(), text, Authored: true));
    }
    if (_problems.Any) {
      return [];
    }
    var tree = LayerTree.Create(_newSchema, [.. all.ConvertAll(layer => layer.Layer)]);
    if (tree is SeedProblems unsound) {
      _problems.Add(unsound.ToString());
      return all;
    }
    _problems.AddRange(tree.OrThrow().IdBlockProblems());
    return all;
  }
}
