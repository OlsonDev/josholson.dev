using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Npgsql;

namespace JoshOlson.Database.Migrate;

// One `seeds:migrate`: reads the lock and the layers, decides what is the input, moves the layers to the newest schema (or
// checks them), and writes the layer files and then the lock, only if every layer passed (ADR 0034, lock file and
// branch-safe migration). `SeedMigrator.MigrateAsync` is its way in.
//
// The input is the working tree, unless the base is needed. The base is what the branch is measured from (`origin/main`, or
// the ref `--base` names), and the lock and the layers are then derived from its copies and the branch's migrations, so a
// migration the branch has not merged can be edited again and again without the lock's checksums getting in the way:
// - the lock and the migrations disagree about a migration the base has not locked (an edit to the branch's own), or
//   `--base` is given: the base's lock and layers are moved through all the migrations after them. A layer the tool wrote is
//   made again; a layer that is somebody's own (see `GeneratedLayers`) is kept as it is and checked against the new schema;
//   one the branch deleted stays deleted.
// - a migration the base has locked has changed or is missing: that is refused, because the base has it as it is.
internal sealed class MigrationRun {
  private readonly NpgsqlConnection _owner;
  private readonly MigrateOptions _options;
  private readonly CancellationToken _cancellationToken;
  private readonly List<string> _warnings = [];
  private Task<SeedSnapshot?>? _snapshot;

  public MigrationRun(NpgsqlConnection owner, MigrateOptions options, CancellationToken cancellationToken) {
    _owner = owner;
    _options = options;
    _cancellationToken = cancellationToken;
  }

  public async Task<MigrateResult> RunAsync() {
    var path = SeedPaths.LockFileIn(_options.MigrationsDirectory);
    var onDisk = await MigrationFiles.LoadAsync(_options.MigrationsDirectory, _cancellationToken);
    var existingText = File.Exists(path) ? await File.ReadAllTextAsync(path, _cancellationToken) : null;
    var previous = existingText is null ? null : SchemaLockParser.Parse(existingText);
    var existing = _options.Relock ? null : previous;
    var check = existing?.Check(onDisk);

    var derivation = await DeriveFromBaseAsync(check, onDisk);
    var problems = new List<string>();
    if (derivation is null && check is not null) {
      problems.AddRange(check.Problems.Select(problem => problem.Message));
      Refuse(problems);
    }
    var (catalog, applied) = await SeedMigrator.InspectAsync(_owner, _options, _cancellationToken);
    problems.AddRange(MigrationHistory.Check(onDisk, applied));
    Refuse(problems);

    var input = derivation?.BaseLock ?? existing;
    var schemaLock = SchemaLockBuilder.Build(input, onDisk, catalog);
    var generated = SeedMigrator.HasLayers(_options)
      ? await GeneratedLayers.FindAsync(_options.SeedsDirectory, previous, SnapshotAsync, _cancellationToken)
      : [];
    var (moved, recorded, kept) = await MoveLayersAsync(input, derivation, check, schemaLock, onDisk, generated, problems);
    Refuse(problems);

    var layers = await WriteLayersAsync(moved);
    schemaLock = schemaLock with { Layers = await RecordLayersAsync(moved, recorded) };
    var text = SchemaLockWriter.Write(schemaLock);
    var changed = !string.Equals(text, existingText, StringComparison.Ordinal);
    if (changed) {
      await SeedMigrator.WriteAsync(path, text, _cancellationToken);
    }
    return new() {
      Path = path,
      PreviousVersion = previous?.Version,
      Version = onDisk.Max(migration => migration.Version),
      Changed = changed,
      Layers = layers,
      Base = derivation?.Snapshot.Description,
      Kept = kept,
      Warnings = _warnings,
    };
  }

  private sealed record Derivation(SchemaLock BaseLock, SeedSnapshot Snapshot);

  // The base, when it is the input: `--base` was given, or the working lock and the migrations disagree. Null when the
  // working tree is the input (or there is no base to read and the disagreement is reported as it is). A base that is not
  // consistent with the migrations on disk is refused.
  private async Task<Derivation?> DeriveFromBaseAsync(LockCheck? check, List<MigrationFile> onDisk) {
    var explicitBase = _options.Base is not null;
    var needed = explicitBase || check is { Problems.Count: > 0 };
    if (_options.Relock || !needed) {
      return null;
    }
    var snapshot = await SnapshotAsync();
    if (snapshot?.LockText is not { } lockText) {
      return explicitBase
        ? throw new SeedFormatException(snapshot is null ? $"There is no base to read ({string.Join(" ", _warnings)})." : $"{snapshot.Description} has no lock, so there is nothing to derive the seeds from.")
        : null;
    }
    var baseLock = SchemaLockParser.Parse(lockText);
    var baseCheck = baseLock.Check(onDisk);
    return baseCheck.Problems.Count > 0
      ? throw new SeedFormatException(BaseProblems(snapshot, baseLock, baseCheck))
      : new(baseLock, snapshot);
  }

  private string BaseProblems(SeedSnapshot snapshot, SchemaLock baseLock, LockCheck baseCheck) {
    var lines = new List<string> { $"{snapshot.Description} has locked migrations your branch does not agree with, and a migration the base has locked cannot change:" };
    lines.AddRange(baseCheck.Problems.Select(problem => $"- {problem.Message}"));
    var missing = baseCheck.Problems
      .Where(problem => problem.Kind is MigrationProblemKind.Missing)
      .Select(problem => baseLock.Migrations.Find(locked => locked.Version == problem.Version)?.File)
      .OfType<string>()
      .ToList();
    if (missing.Count > 0) {
      lines.Add($"To bring the base's migrations in without merging the rest: git checkout {snapshot.Commit} -- {string.Join(" ", missing.Select(file => Path.Combine(_options.MigrationsDirectory, file)))}");
    }
    return string.Join("\n", lines);
  }

  // The layers after the run, which of them are the tool's to record, and which were kept as somebody's own.
  private async Task<(List<MovedLayer> Moved, HashSet<string> Recorded, List<string> Kept)> MoveLayersAsync(
    SchemaLock? input,
    Derivation? derivation,
    LockCheck? check,
    SchemaLock schemaLock,
    List<MigrationFile> onDisk,
    HashSet<string> generated,
    List<string> problems
  ) {
    try {
      if (derivation is not null) {
        return await DeriveLayersAsync(derivation, schemaLock, onDisk, generated);
      }
      if (input is not null && check is { Pending.Count: > 0 } && SeedMigrator.HasLayers(_options)) {
        var oldTree = await LoadWorkingTreeAsync(input);
        return (await LayerMover.MoveAsync(_owner, _options, input, schemaLock, onDisk, oldTree, authored: [], dropped: [], _cancellationToken), generated, []);
      }
    } catch (SeedFormatException exception) {
      problems.Add(exception.Message);
      return ([], [], []);
    }
    problems.AddRange(await SeedMigrator.CheckLayersAsync(_options, schemaLock, _cancellationToken));
    return ([], generated, []);
  }

  // The base's layers moved through the migrations after its lock. The layers in the working tree decide what becomes of
  // each: one the tool wrote (or that is the base's own copy) is made again; one that is somebody's own is kept as it is,
  // whether the base has a layer of that name or not; one the base has and the working tree does not was deleted on the
  // branch.
  private async Task<(List<MovedLayer> Moved, HashSet<string> Recorded, List<string> Kept)> DeriveLayersAsync(
    Derivation derivation,
    SchemaLock schemaLock,
    List<MigrationFile> onDisk,
    HashSet<string> generated
  ) {
    var working = new Dictionary<string, string>(StringComparer.Ordinal);
    if (Directory.Exists(_options.SeedsDirectory)) {
      foreach (var path in LayerLoader.Files(_options.SeedsDirectory)) {
        working[Path.GetFileNameWithoutExtension(path)] = await File.ReadAllTextAsync(path, _cancellationToken);
      }
    }
    var remade = derivation.Snapshot.Layers.Keys.Where(name => working.ContainsKey(name) && generated.Contains(name)).ToHashSet(StringComparer.Ordinal);
    var authored = working.Where(entry => !remade.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    var dropped = derivation.Snapshot.Layers.Keys.Where(name => !working.ContainsKey(name)).ToHashSet(StringComparer.Ordinal);
    LayerTree oldTree;
    try {
      oldTree = LayerLoader.Load(derivation.Snapshot.Layers, new SeedSchema(derivation.BaseLock));
    } catch (SeedFormatException exception) {
      throw new SeedFormatException($"{derivation.Snapshot.Description}'s layers do not read against its own lock:\n{exception.Message}", exception);
    }
    var moved = await LayerMover.MoveAsync(_owner, _options, derivation.BaseLock, schemaLock, onDisk, oldTree, authored, dropped, _cancellationToken);
    return (moved, remade, [.. authored.Keys.Order(StringComparer.Ordinal)]);
  }

  private async Task<LayerTree> LoadWorkingTreeAsync(SchemaLock input) {
    try {
      return await LayerLoader.LoadAsync(_options.SeedsDirectory, new SeedSchema(input), _cancellationToken);
    } catch (SeedFormatException exception) {
      throw new SeedFormatException(
        $"The layers do not read against the schema they were written for (version {input.Version.ToInvariantString()}):\n{exception.Message}",
        exception);
    }
  }

  // Only the layers whose text changed are rewritten, so a migration that leaves a layer as it was leaves its file alone, and
  // a layer that is somebody's own is never written.
  private async Task<List<(string Layer, bool Rewritten)>> WriteLayersAsync(List<MovedLayer> moved) {
    var layers = new List<(string Layer, bool Rewritten)>(moved.Count);
    foreach (var layer in moved.Where(layer => !layer.Authored)) {
      var path = LayerLoader.PathOf(_options.SeedsDirectory, layer.Layer.Name);
      var rewritten = !File.Exists(path) || !string.Equals(await File.ReadAllTextAsync(path, _cancellationToken), layer.Yaml, StringComparison.Ordinal);
      if (rewritten) {
        await SeedMigrator.WriteAsync(path, layer.Yaml, _cancellationToken);
      }
      layers.Add((layer.Layer.Name, rewritten));
    }
    return layers;
  }

  // What the lock says about the layer files once this run is done: only the tool's own, each as it is now (a moved
  // layer as it was just written, any other as it is on disk), by name. Null when there are no layers at all. A layer
  // somebody wrote, or edited since the tool wrote it, is left out, so it stays theirs.
  private async Task<List<LockedLayer>?> RecordLayersAsync(List<MovedLayer> moved, HashSet<string> recorded) {
    if (!SeedMigrator.HasLayers(_options)) {
      return null;
    }
    var layers = new List<LockedLayer>(recorded.Count);
    foreach (var path in LayerLoader.Files(_options.SeedsDirectory)) {
      var name = Path.GetFileNameWithoutExtension(path);
      if (!recorded.Contains(name)) {
        continue;
      }
      var text = moved.Find(layer => !layer.Authored && string.Equals(layer.Layer.Name, name, StringComparison.Ordinal))?.Yaml;
      layers.Add(new(name, text is null ? LayerChecksum.Of(await File.ReadAllBytesAsync(path, _cancellationToken)) : LayerChecksum.OfText(text)));
    }
    return layers;
  }

  // The base, read the first time it is asked for and not again; what the source said about it is kept for the result.
  private Task<SeedSnapshot?> SnapshotAsync()
    => _snapshot ??= ReadSnapshotAsync();

  private async Task<SeedSnapshot?> ReadSnapshotAsync() {
    if (_options.Snapshots is null) {
      return null;
    }
    var read = await _options.Snapshots.ReadAsync(_options.Base, _options.Fetch, _cancellationToken);
    _warnings.AddRange(read.Warnings);
    return read.Snapshot;
  }

  private void Refuse(List<string> problems) {
    if (problems.Count > 0) {
      throw new SeedFormatException($"seeds:migrate did not write {SeedPaths.LockFileName}:\n- {string.Join("\n- ", problems.Concat(_warnings))}");
    }
  }
}
