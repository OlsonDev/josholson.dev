#if INCLUDE_TESTS
using System.Text.RegularExpressions;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Api.Common.Db;

// Maps a test scope's ID to a data source for its database (ADR 0033, a scoped database per test). The ID is not a
// connection string and not a database name: it is lowercase letters and digits, and the database is `seed_test_<id>`,
// the name the seed tool and the browser tests give the clones they make. An ID whose database does not exist is
// unknown. Every lookup asks the server, so a dropped database is unknown at once and its data source is disposed, and
// a database dropped and made again under the same ID (a hand-picked one: `seed:test 42`) is told apart by its `oid`
// and gets a new data source, since the old one's pooled connections died with the database it was for.
// Each data source has a small pool: a run has many scopes at once, and none of them needs ten warm connections.
public sealed partial class ScopedDatabaseRegistry : IAsyncDisposable {
  // `SeedDatabaseNames.Test` in JoshOlson.Database, which the API does not reference: a test of the whole path
  // (`ScopedDatabaseMiddleware`) uses a database made with this name.
  public const string Prefix = "seed_test_";
  private const int _MaxPoolSize = 10;

  private readonly NpgsqlDataSource _catalog;
  private readonly NpgsqlConnectionStringBuilder _template;
  private readonly Lock _lock = new();
  private readonly Dictionary<string, Entry> _entries = [with(StringComparer.Ordinal)];

  // `catalog` is the default data source, which answers whether a database exists; `connectionString` is the default
  // connection string, whose server and credentials every scope shares.
  public ScopedDatabaseRegistry(NpgsqlDataSource catalog, string connectionString) {
    _catalog = catalog;
    _template = new NpgsqlConnectionStringBuilder(connectionString) {
      MinPoolSize = 0,
      MaxPoolSize = _MaxPoolSize,
    };
  }

  // Lowercase letters and digits, at most 40, like the seed tool's test IDs.
  [GeneratedRegex("^[a-z0-9]{1,40}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
  private static partial Regex ValidId();

  public static bool IsValidId(string id)
    => ValidId().IsMatch(id);

  public static string DatabaseName(string id)
    => Prefix + id;

  // The data source for the scope's database, or null for an ID that is not valid or has no database.
  public async Task<NpgsqlDataSource?> FindAsync(string id, CancellationToken cancellationToken) {
    if (!IsValidId(id)) return null;
    var oid = await DatabaseOidAsync(DatabaseName(id), cancellationToken);

    Entry? current = null;
    Entry? replaced = null;
    lock (_lock) {
      _ = _entries.TryGetValue(id, out var existing);
      if (oid is null) {
        replaced = existing;
        _ = _entries.Remove(id);
      } else if (existing?.Oid == oid) {
        current = existing;
      } else {
        replaced = existing;
        current = new Entry(oid.Value, new Lazy<NpgsqlDataSource>(() => Build(id)));
        _entries[id] = current;
      }
    }
    if (replaced is not null) await replaced.DisposeAsync();
    return current?.DataSource.Value;
  }

  // The database's `oid`, which a database made again under the same name does not keep, or null when there is none.
  private async Task<long?> DatabaseOidAsync(string name, CancellationToken cancellationToken) {
    var sql = """
      SELECT d.oid::bigint  AS database_oid
      FROM (SELECT)         AS _
      JOIN pg_database      AS d  ON d.datname = @name;
      """;
    var parameters = new { name };
    await using var connection = await _catalog.OpenConnectionAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<long?>(sql, parameters, cancellationToken);
  }

  private NpgsqlDataSource Build(string id) {
    var builder = new NpgsqlConnectionStringBuilder(_template.ConnectionString) { Database = DatabaseName(id) };
    return new NpgsqlDataSourceBuilder(builder.ConnectionString).UseNodaTime().Build();
  }

  public async ValueTask DisposeAsync() {
    List<Entry> entries;
    lock (_lock) {
      entries = [.. _entries.Values];
      _entries.Clear();
    }
    foreach (var entry in entries) await entry.DisposeAsync();
  }

  // A data source built for the database with this `oid`. It is built when first asked for, not when the lookup that
  // found the database ran, so an ID that is looked up and never used opens no pool.
  private sealed record Entry(long Oid, Lazy<NpgsqlDataSource> DataSource) {
    public ValueTask DisposeAsync()
      => DataSource.IsValueCreated ? DataSource.Value.DisposeAsync() : ValueTask.CompletedTask;
  }
}
#endif
