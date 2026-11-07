using System.Text;
using JoshOlson.Database.Catalog;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Npgsql;

namespace JoshOlson.Database.Migrate;

public static class SeedMigrator {
  public const string LockFileName = "schema.lock.yaml";

  // `seeds:migrate`, as far as it is built: makes `schema.lock.yaml` describe the database, which must have applied
  // exactly the migrations on disk. The lock is only written if everything checks out, and every problem is reported
  // together. Moving seed layers to a new migration is not built yet, so with any layer present this refuses instead
  // of leaving the layers behind the lock; with none, restamping the lock is the whole migration.
  public static async Task<MigrateResult> MigrateAsync(NpgsqlConnection connection, MigrateOptions options, CancellationToken cancellationToken) {
    var path = Path.Combine(options.MigrationsDirectory, LockFileName);
    var onDisk = await MigrationFiles.LoadAsync(options.MigrationsDirectory, cancellationToken);
    var existingText = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    var existing = existingText is null ? null : SchemaLockParser.Parse(existingText);

    var problems = new List<string>();
    if (existing is not null) {
      problems.AddRange(existing.Check(onDisk).Problems.Select(problem => problem.Message));
    }
    if (Directory.Exists(options.SeedsDirectory)) {
      var layers = LayerLoader.Files(options.SeedsDirectory).Count;
      if (layers > 0) {
        problems.Add($"The seeds have {layers.ToInvariantString()} layer(s), and moving layers to a new migration is not built yet. Until it is, seeds:migrate only makes the lock while there are none.");
      }
    }
    problems.AddRange(MigrationHistory.Check(onDisk, await MigrationHistory.ReadAsync(connection, cancellationToken)));
    if (problems.Count > 0) {
      throw new SeedFormatException($"seeds:migrate did not write {LockFileName}:\n- {string.Join("\n- ", problems)}");
    }

    var catalog = await SchemaCatalog.ReadAsync(connection, new(), cancellationToken);
    var text = SchemaLockWriter.Write(SchemaLockBuilder.Build(existing, onDisk, catalog));
    var changed = !string.Equals(text, existingText, StringComparison.Ordinal);
    if (changed) {
      await WriteAsync(path, text, cancellationToken);
    }
    return new() {
      Path = path,
      PreviousVersion = existing?.Version,
      Version = onDisk.Max(migration => migration.Version),
      Changed = changed,
    };
  }

  // Written beside the lock and moved over it, so an interrupted run leaves the old lock and not half of the new one.
  private static async Task WriteAsync(string path, string text, CancellationToken cancellationToken) {
    var temporary = path + ".tmp";
    await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    File.Move(temporary, path, overwrite: true);
  }
}
