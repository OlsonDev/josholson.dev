using System.Text;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Catalog;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Npgsql;

namespace JoshOlson.Database.Migrate;

public static class SeedMigrator {
  public const string LockFileName = "schema.lock.yaml";

  // `seeds:check`, the lock's half: the lock has to exist, describe the migrations on disk (none changed, renumbered, or
  // added behind it), and be at the newest one, so the layers are migrated to everything there is. Returns every problem
  // together, empty when the seeds are current.
  public static async Task<List<string>> CheckLockAsync(string migrationsDirectory, CancellationToken cancellationToken) {
    var path = Path.Combine(migrationsDirectory, LockFileName);
    if (!File.Exists(path)) {
      return [$"{path} does not exist. Run mise run seeds:migrate."];
    }
    var onDisk = await MigrationFiles.LoadAsync(migrationsDirectory, cancellationToken);
    var check = SchemaLockParser.Parse(await File.ReadAllTextAsync(path, cancellationToken)).Check(onDisk);
    var problems = check.Problems.ConvertAll(problem => problem.Message);
    if (check.Pending.Count > 0) {
      problems.Add(
        $"{string.Join(", ", check.Pending.Select(migration => migration.File))} {(check.Pending.Count is 1 ? "is" : "are")} newer than the seeds. "
        + "Run mise run seeds:migrate.");
    }
    return problems;
  }

  // `seeds:migrate`, as far as it is built: makes `schema.lock.yaml` describe the schema the migrations on disk produce,
  // which is read from the template for the newest migration (built if it is not there; the dev database is not used).
  // The lock is only written if everything checks out, and every problem is reported together. When the lock is behind
  // the migrations and there are layers, they are moved to the newest schema first (`LayerMover`) and the files and
  // then the lock are written only if every one of them moved and builds; otherwise, they are checked against the new
  // schema (`CheckLayersAsync`), so the lock never moves ahead of a layer it would break.
  // `Relock` starts the lock over instead of carrying the old one forward (see `MigrateOptions.Relock`).
  public static async Task<MigrateResult> MigrateAsync(NpgsqlConnection owner, MigrateOptions options, CancellationToken cancellationToken) {
    var path = Path.Combine(options.MigrationsDirectory, LockFileName);
    var onDisk = await MigrationFiles.LoadAsync(options.MigrationsDirectory, cancellationToken);
    var existingText = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    var existing = existingText is null ? null : SchemaLockParser.Parse(existingText);
    var previous = existing;
    if (options.Relock) {
      existing = null;
    }
    // Which layer files are the tool's own, by what the lock recorded last time (even when it is being started over).
    var generated = HasLayers(options) ? await GeneratedLayers.FindAsync(options.SeedsDirectory, previous, cancellationToken) : [];

    var problems = new List<string>();
    var check = existing?.Check(onDisk);
    if (check is not null) {
      problems.AddRange(check.Problems.Select(problem => problem.Message));
    }
    ThrowIfAny(problems);

    var (catalog, applied) = await InspectAsync(owner, options, cancellationToken);
    problems.AddRange(MigrationHistory.Check(onDisk, applied));
    ThrowIfAny(problems);

    var schemaLock = SchemaLockBuilder.Build(existing, onDisk, catalog);
    var moved = new List<MovedLayer>();
    if (existing is not null && check is { Pending.Count: > 0 } && HasLayers(options)) {
      try {
        moved = await LayerMover.MoveAsync(owner, options, existing, schemaLock, onDisk, cancellationToken);
      } catch (SeedFormatException exception) {
        problems.Add(exception.Message);
      }
    } else {
      problems.AddRange(await CheckLayersAsync(options, schemaLock, cancellationToken));
    }
    ThrowIfAny(problems);
    var layers = await WriteLayersAsync(options, moved, cancellationToken);
    schemaLock = schemaLock with { Layers = await RecordLayersAsync(options, moved, generated, cancellationToken) };

    var text = SchemaLockWriter.Write(schemaLock);
    var changed = !string.Equals(text, existingText, StringComparison.Ordinal);
    if (changed) {
      await WriteAsync(path, text, cancellationToken);
    }
    return new() {
      Path = path,
      PreviousVersion = previous?.Version,
      Version = onDisk.Max(migration => migration.Version),
      Changed = changed,
      Layers = layers,
    };
  }

  // The catalog and Waypoint's history of the template for the newest migration. The template refuses connections, so a
  // clone is read and dropped.
  private static async Task<(CatalogSchema Catalog, List<AppliedMigration> Applied)> InspectAsync(
    NpgsqlConnection owner,
    MigrateOptions options,
    CancellationToken cancellationToken
  ) {
    var (template, _) = await options.Template.EnsureAsync(owner, cancellationToken);
    var clone = $"{SeedDatabaseNames.Prefix}migrate_{Guid.NewGuid():N}";
    await options.Databases.CloneAsync(owner, template, clone, cancellationToken);
    try {
      await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(clone));
      await connection.OpenAsync(cancellationToken);
      return (
        await SchemaCatalog.ReadAsync(connection, new(), cancellationToken),
        await MigrationHistory.ReadAsync(connection, cancellationToken));
    } finally {
      await options.Databases.DropAsync(owner, clone, CancellationToken.None);
    }
  }

  // Only the layers whose text changed are rewritten, so a migration that leaves a layer as it was leaves its file alone.
  private static async Task<List<(string Layer, bool Rewritten)>> WriteLayersAsync(
    MigrateOptions options,
    List<MovedLayer> moved,
    CancellationToken cancellationToken
  ) {
    var layers = new List<(string Layer, bool Rewritten)>(moved.Count);
    foreach (var layer in moved) {
      var path = Path.Combine(options.SeedsDirectory, layer.Layer.Name + ".yaml");
      var rewritten = !string.Equals(await File.ReadAllTextAsync(path, cancellationToken), layer.Yaml, StringComparison.Ordinal);
      if (rewritten) {
        await WriteAsync(path, layer.Yaml, cancellationToken);
      }
      layers.Add((layer.Layer.Name, rewritten));
    }
    return layers;
  }

  private static void ThrowIfAny(List<string> problems) {
    if (problems.Count > 0) {
      throw new SeedFormatException($"seeds:migrate did not write {LockFileName}:\n- {string.Join("\n- ", problems)}");
    }
  }

  // What the lock says about the layer files once this run is done: only the tool's own, each as it is now (a moved
  // layer as it was just written, any other as it is on disk), by name. Null when there are no layers at all. A layer
  // somebody wrote, or edited since the tool wrote it, is left out, so it stays theirs.
  private static async Task<List<LockedLayer>?> RecordLayersAsync(
    MigrateOptions options,
    List<MovedLayer> moved,
    HashSet<string> generated,
    CancellationToken cancellationToken
  ) {
    if (!HasLayers(options)) {
      return null;
    }
    var recorded = new List<LockedLayer>(generated.Count);
    foreach (var path in LayerLoader.Files(options.SeedsDirectory)) {
      var name = Path.GetFileNameWithoutExtension(path);
      if (!generated.Contains(name)) {
        continue;
      }
      var text = moved.Find(layer => string.Equals(layer.Layer.Name, name, StringComparison.Ordinal))?.Yaml;
      recorded.Add(new(name, text is null ? LayerChecksum.Of(await File.ReadAllBytesAsync(path, cancellationToken)) : LayerChecksum.OfText(text)));
    }
    return recorded;
  }

  private static bool HasLayers(MigrateOptions options)
    => Directory.Exists(options.SeedsDirectory) && LayerLoader.Files(options.SeedsDirectory).Count > 0;

  // The layers are not rewritten, only checked: each still has to read as its columns' types in the new schema and
  // apply to a database built from the new migrations (`LayerCheck`). A layer a migration broke is reported, and fixing
  // it is by hand until moving layers is built. With no layers there is nothing to check.
  private static async Task<List<string>> CheckLayersAsync(MigrateOptions options, SchemaLock schemaLock, CancellationToken cancellationToken) {
    var layers = Directory.Exists(options.SeedsDirectory) ? LayerLoader.Files(options.SeedsDirectory).Count : 0;
    if (layers is 0) {
      return [];
    }
    return options.LayerCheck is { } check
      ? await check(new SeedSchema(schemaLock), cancellationToken)
      : [$"The seeds have {layers.ToInvariantString()} layer(s), and there is no way to check them against the new schema here."];
  }

  // Written beside the lock and moved over it, so an interrupted run leaves the old lock and not half of the new one.
  private static async Task WriteAsync(string path, string text, CancellationToken cancellationToken) {
    var temporary = path + ".tmp";
    await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    File.Move(temporary, path, overwrite: true);
  }
}
