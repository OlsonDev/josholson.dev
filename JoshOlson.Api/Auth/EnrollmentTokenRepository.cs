using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;

namespace JoshOlson.Api.Auth;

public sealed class EnrollmentTokenRepository {
  private readonly NpgsqlConnection _connection;

  public EnrollmentTokenRepository(NpgsqlConnection connection)
    => _connection = connection;

  public async Task<int> CreateAsync(
    int userId,
    byte[] tokenHash,
    Instant expiresUtc,
    CancellationToken cancellationToken
  ) {
    var sql = """
      INSERT INTO auth.enrollment_token (
          user_id
        , token_hash
        , expires_utc
      ) VALUES (
          @userId
        , @tokenHash
        , @expiresUtc
      )
      RETURNING enrollment_token_id;
      """;
    var parameters = new {
      userId,
      tokenHash,
      expiresUtc,
    };
    return await _connection.QuerySingleAsync<int>(sql, parameters, cancellationToken);
  }

  // The user the token enrolls, if it is unused and not expired. Read-only: it stays redeemable until `UseAsync`.
  public async Task<int?> FindUserIdAsync(byte[] tokenHash, Instant nowUtc, CancellationToken cancellationToken) {
    var sql = """
      SELECT
          t.user_id  AS user_id
      FROM (SELECT)               AS _
      JOIN auth.enrollment_token  AS t  ON t.token_hash   = @tokenHash
                                       AND t.used_utc     IS NULL
                                       AND t.expires_utc  > @nowUtc;
      """;
    var parameters = new {
      tokenHash,
      nowUtc,
    };
    return await _connection.QuerySingleOrDefaultAsync<int?>(sql, parameters, cancellationToken);
  }

  // Uses the token up. False when it was already used, expired, or unknown, so two requests cannot both win.
  public async Task<bool> UseAsync(byte[] tokenHash, Instant nowUtc, CancellationToken cancellationToken) {
    var sql = """
      UPDATE auth.enrollment_token AS t SET
          used_utc  = @nowUtc
      WHERE t.token_hash   = @tokenHash
        AND t.used_utc     IS NULL
        AND t.expires_utc  > @nowUtc;
      """;
    var parameters = new {
      tokenHash,
      nowUtc,
    };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }
}
