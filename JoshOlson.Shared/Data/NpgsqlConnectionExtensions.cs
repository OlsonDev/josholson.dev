using Dapper;
using Npgsql;

namespace JoshOlson.Shared.Data;

// Dapper calls that always carry the cancellation token, so a repository passes the SQL, the parameters, and the
// token instead of building a CommandDefinition for each query. The token is required: Dapper's own overloads without
// one would compile but drop it.
public static class NpgsqlConnectionExtensions {
  extension(NpgsqlConnection connection) {
    // Buffered, so Dapper has already built a `List<T>`; `AsList` hands it back without a copy.
    public async Task<List<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken)
      => (await connection.QueryAsync<T>(new CommandDefinition(sql, cancellationToken: cancellationToken))).AsList();

    public async Task<List<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
      => (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)))
        .AsList();

    // For rows that are looked up by a key in code, such as a parent's rows or its children's. The first column is the
    // key, and everything from the column named `splitOn` on fills a `T`. The key stays out of `T`, so a model type
    // carries no database ids. One row per key: a key that comes back twice throws.
    public async Task<Dictionary<TKey, T>> QueryDictionaryAsync<TKey, T>(
      string sql,
      object? parameters,
      string splitOn,
      CancellationToken cancellationToken
    ) where TKey : notnull
      => (await QueryPairsAsync<TKey, T>(connection, sql, parameters, splitOn, cancellationToken))
        .ToDictionary(pair => pair.Key, pair => pair.Item);

    // The same, for any number of rows per key (a table's columns): each key's list is in the order the rows came back.
    public async Task<Dictionary<TKey, List<T>>> QueryGroupedAsync<TKey, T>(
      string sql,
      object? parameters,
      string splitOn,
      CancellationToken cancellationToken
    ) where TKey : notnull {
      Dictionary<TKey, List<T>> groups = [];
      foreach (var (key, item) in await QueryPairsAsync<TKey, T>(connection, sql, parameters, splitOn, cancellationToken)) {
        if (!groups.TryGetValue(key, out var list)) {
          list = [];
          groups[key] = list;
        }
        list.Add(item);
      }
      return groups;
    }

    public Task<T> QuerySingleAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
      => connection.QuerySingleAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
      => connection.QuerySingleOrDefaultAsync<T>(
        new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    public Task<SqlMapper.GridReader> QueryMultipleAsync(
      string sql,
      object? parameters,
      CancellationToken cancellationToken
    )
      => connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken)
      => connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
  }

  private static async Task<List<(TKey Key, T Item)>> QueryPairsAsync<TKey, T>(
    NpgsqlConnection connection,
    string sql,
    object? parameters,
    string splitOn,
    CancellationToken cancellationToken
  )
    => (await connection.QueryAsync<TKey, T, (TKey, T)>(
      new CommandDefinition(sql, parameters, cancellationToken: cancellationToken),
      (key, item) => (key, item),
      splitOn)).AsList();
}
