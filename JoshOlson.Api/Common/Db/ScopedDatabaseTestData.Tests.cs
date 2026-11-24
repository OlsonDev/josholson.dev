using JoshOlson.Api.Common.Testing;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Api.Common.Db;

// Empty databases named like a test scope's (`seed_test_<id>`), made as the owner and dropped when the test ends. They have
// no tables, which is all the scope's own tests need: `current_database()` says which one a connection is on. A test
// takes this in its constructor: `Startup` registers every `*TestData` class as scoped.
public sealed class ScopedDatabaseTestData : IAsyncDisposable {
  private readonly List<string> _ids = [];

  // A new scope ID, and its database.
  public async Task<string> CreateAsync(CancellationToken cancellationToken) {
    var id = Guid.NewGuid().ToString("N");
    _ids.Add(id);
    await ExecuteAsOwnerAsync($"CREATE DATABASE {Quoted(id)};", cancellationToken);
    return id;
  }

  // The same ID again, as a new database: what `seed:test 42` does after a drop.
  public async Task RecreateAsync(string id, CancellationToken cancellationToken) {
    await DropAsync(id, cancellationToken);
    await ExecuteAsOwnerAsync($"CREATE DATABASE {Quoted(id)};", cancellationToken);
  }

  public async Task DropAsync(string id, CancellationToken cancellationToken)
    => await ExecuteAsOwnerAsync($"DROP DATABASE IF EXISTS {Quoted(id)} WITH (FORCE);", cancellationToken);

  public async ValueTask DisposeAsync() {
    foreach (var id in _ids) await DropAsync(id, CancellationToken.None);
  }

  private static string Quoted(string id)
    => ScopedDatabaseRegistry.IsValidId(id)
      ? $"\"{ScopedDatabaseRegistry.DatabaseName(id)}\""
      : throw new ArgumentException("Not a scope ID.", nameof(id));

  private static async Task ExecuteAsOwnerAsync(string sql, CancellationToken cancellationToken) {
    await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnectionString);
    await connection.OpenAsync(cancellationToken);
    _ = await connection.ExecuteAsync(sql, cancellationToken);
  }
}
