using JoshOlson.Api.Common.Extensions;
using Npgsql;

namespace JoshOlson.Api.Auth;

public sealed class AuthUserRepository {
  private readonly NpgsqlConnection _connection;

  public AuthUserRepository(NpgsqlConnection connection)
    => _connection = connection;

  public async Task<AuthUser?> GetAsync(int userId, CancellationToken cancellationToken)
    => await GetByConditionAsync("u.user_id = @userId", new { userId }, cancellationToken);

  public async Task<AuthUser?> GetByHandleAsync(byte[] userHandle, CancellationToken cancellationToken)
    => await GetByConditionAsync("u.user_handle = @userHandle", new { userHandle }, cancellationToken);

  // The one place the select list lives. `onCondition` is SQL the code wrote, never request data; values go in
  // `parameters`.
  private async Task<AuthUser?> GetByConditionAsync(
    string onCondition, object parameters, CancellationToken cancellationToken
  ) {
    var sql = $"""
      SELECT
          u.user_id       AS user_id
        , u.display_name  AS display_name
        , u.user_handle   AS user_handle
        , u.is_active     AS is_active
      FROM (SELECT)   AS _
      JOIN auth.user  AS u  ON {onCondition};
      """;
    return await _connection.QuerySingleOrDefaultAsync<AuthUser>(sql, parameters, cancellationToken);
  }

  public async Task<int> CreateAsync(string displayName, byte[] userHandle, CancellationToken cancellationToken) {
    var sql = """
      INSERT INTO auth.user (
          display_name
        , user_handle
      ) VALUES (
          @displayName
        , @userHandle
      )
      RETURNING user_id;
      """;
    var parameters = new {
      displayName,
      userHandle,
    };
    return await _connection.QuerySingleAsync<int>(sql, parameters, cancellationToken);
  }
}
