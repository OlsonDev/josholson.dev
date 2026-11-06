using JoshOlson.Database.Locks;
using JoshOlson.Shared.Data;
using JoshOlson.Shared.Extensions;
using Npgsql;

namespace JoshOlson.Database.Migrate;

// What the database has applied, against what is on disk. Waypoint's own checksum is not SHA-256 of the file, so the
// history can say which migrations ran and under what file name, and not whether the text is the same: the lock's
// checksums cover that.
public static class MigrationHistory {
  // The versioned migrations Waypoint recorded, oldest first. A database Waypoint has never run against has none.
  public static async Task<List<AppliedMigration>> ReadAsync(NpgsqlConnection connection, CancellationToken cancellationToken) {
    var exists = """
      SELECT
          to_regclass('public.waypoint_schema_history') IS NOT NULL  AS history_exists
      FROM (SELECT) AS _;
      """;
    if (!await connection.QuerySingleAsync<bool>(exists, parameters: null, cancellationToken)) {
      return [];
    }

    var sql = """
      SELECT
          h.version::int  AS version
        , h.script        AS file
        , h.success       AS success
      FROM (SELECT) AS _
      JOIN public.waypoint_schema_history  AS h  ON h.type     = 'SQL'
                                                AND h.version IS NOT NULL
      ORDER BY h.installed_rank;
      """;
    return await connection.QueryAsync<AppliedMigration>(sql, parameters: null, cancellationToken);
  }

  // Every way the database is not at the head of the migrations on disk, one message each, by version. Empty means the
  // database was built from exactly these migrations, so its catalog is the schema they produce.
  public static List<string> Check(List<MigrationFile> onDisk, List<AppliedMigration> applied) {
    var disk = onDisk.ToDictionary(migration => migration.Version);
    var appliedByVersion = applied.ToDictionary(migration => migration.Version);
    var problems = new List<(int Version, string Message)>();

    foreach (var migration in applied) {
      var version = migration.Version.ToInvariantString();
      if (!migration.Success) {
        problems.Add((migration.Version, $"The database recorded {migration.File} (version {version}) as failed. Repair it with Waypoint or rebuild the dev database."));
      } else if (!disk.TryGetValue(migration.Version, out var current)) {
        problems.Add((migration.Version, $"The database has {migration.File} (version {version}), which your migrations do not have. Rebuild the dev database from your migrations."));
      } else if (!string.Equals(current.File, migration.File, StringComparison.Ordinal)) {
        problems.Add((migration.Version, $"Your version {version} is {current.File}, but the database applied {migration.File}. Rebuild the dev database from your migrations."));
      }
    }
    foreach (var migration in onDisk.Where(migration => !appliedByVersion.ContainsKey(migration.Version))) {
      problems.Add((migration.Version, $"{migration.File} (version {migration.Version.ToInvariantString()}) has not been applied to the database. Run `mise run waypoint migrate`."));
    }

    problems.Sort((left, right) => left.Version.CompareTo(right.Version));
    return [.. problems.Select(problem => problem.Message)];
  }
}
