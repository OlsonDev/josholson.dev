using Dapper;
using Npgsql;

namespace JoshOlson.Api.Common.Extensions;

// Dapper calls that always carry the cancellation token, so a repository passes the SQL, the parameters, and the
// token instead of building a CommandDefinition for each query. The token is required: Dapper's own overloads without
// one would compile but drop it.
public static class NpgsqlConnectionExtensions {
  extension(NpgsqlConnection connection) {
    public Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken)
      => connection.QueryAsync<T>(new CommandDefinition(sql, cancellationToken: cancellationToken));

    public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
      => connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    public Task<T> QuerySingleAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
      => connection.QuerySingleAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
      => connection.QuerySingleOrDefaultAsync<T>(
        new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    public Task<SqlMapper.GridReader> QueryMultipleAsync(
      string sql, object? parameters, CancellationToken cancellationToken
    )
      => connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken)
      => connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
  }
}
