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

  // `seeds:migrate`: makes `schema.lock.yaml` describe the schema the migrations on disk produce, which is read from the
  // template for the newest migration (built if it is not there; the dev database is not used). The lock is only written if
  // everything checks out, and every problem is reported together. When the lock is behind the migrations and there are
  // layers, they are moved to the newest schema first (`LayerMover`) and the files and then the lock are written only if
  // every one of them moved and builds; otherwise, they are checked against the new schema (`CheckLayersAsync`), so the lock
  // never moves ahead of a layer it would break. When the base is needed (a branch re-editing a migration it has not merged,
  // or `--base`) the lock and layers are derived from the base's instead (`MigrationRun`).
  // `Relock` starts the lock over instead of carrying the old one forward (see `MigrateOptions.Relock`).
  public static async Task<MigrateResult> MigrateAsync(NpgsqlConnection owner, MigrateOptions options, CancellationToken cancellationToken)
    => await new MigrationRun(owner, options, cancellationToken).RunAsync();

  // The catalog and Waypoint's history of the template for the newest migration. The template refuses connections, so a
  // clone is read and dropped.
  internal static async Task<(CatalogSchema Catalog, List<AppliedMigration> Applied)> InspectAsync(
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

  internal static bool HasLayers(MigrateOptions options)
    => Directory.Exists(options.SeedsDirectory) && LayerLoader.Files(options.SeedsDirectory).Count > 0;

  // The layers are not rewritten, only checked: each still has to read as its columns' types in the new schema and
  // apply to a database built from the new migrations (`LayerCheck`). A layer a migration broke is reported, and fixing
  // it is by hand. With no layers there is nothing to check.
  internal static async Task<List<string>> CheckLayersAsync(MigrateOptions options, SchemaLock schemaLock, CancellationToken cancellationToken) {
    var layers = Directory.Exists(options.SeedsDirectory) ? LayerLoader.Files(options.SeedsDirectory).Count : 0;
    if (layers is 0) {
      return [];
    }
    return options.LayerCheck is { } check
      ? await check(new SeedSchema(schemaLock), cancellationToken)
      : [$"The seeds have {layers.ToInvariantString()} layer(s), and there is no way to check them against the new schema here."];
  }

  // Written beside the file and moved over it, so an interrupted run leaves the old file and not half of the new one.
  internal static async Task WriteAsync(string path, string text, CancellationToken cancellationToken) {
    var temporary = path + ".tmp";
    await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    File.Move(temporary, path, overwrite: true);
  }
}
