using JoshOlson.Api.Common.Extensions;
using NodaTime;
using Npgsql;

namespace JoshOlson.Api.Auth;

public sealed class SessionRepository {
  private readonly NpgsqlConnection _connection;

  public SessionRepository(NpgsqlConnection connection)
    => _connection = connection;

  public async Task<int> CreateAsync(NewSession session, CancellationToken cancellationToken) {
    var sql = """
      INSERT INTO auth.session (
          user_id
        , token_hash
        , expires_utc
        , ip
        , user_agent_id
      ) VALUES (
          @userId
        , @tokenHash
        , @expiresUtc
        , @ip
        , @userAgentId
      )
      RETURNING session_id;
      """;
    var parameters = new {
      session.UserId,
      session.TokenHash,
      session.ExpiresUtc,
      session.Ip,
      session.UserAgentId,
    };
    return await _connection.QuerySingleAsync<int>(sql, parameters, cancellationToken);
  }

  // Only a session that has not expired and whose user is active.
  public async Task<SessionInfo?> GetActiveAsync(
    byte[] tokenHash,
    Instant nowUtc,
    CancellationToken cancellationToken
  ) {
    var sql = """
      SELECT
          s.session_id   AS session_id
        , s.user_id      AS user_id
        , s.created_utc  AS created_utc
        , s.expires_utc  AS expires_utc
      FROM (SELECT)      AS _
      JOIN auth.session  AS s  ON s.token_hash   = @tokenHash
                              AND s.expires_utc  > @nowUtc
      JOIN auth.user     AS u  ON u.user_id      = s.user_id
                              AND u.is_active    = TRUE;
      """;
    var parameters = new {
      tokenHash,
      nowUtc,
    };
    return await _connection.QuerySingleOrDefaultAsync<SessionInfo>(sql, parameters, cancellationToken);
  }

  // Slides the idle expiration forward.
  public async Task<bool> ExtendExpirationAsync(
    int sessionId,
    Instant seenUtc,
    Instant expiresUtc,
    CancellationToken cancellationToken
  ) {
    var sql = """
      UPDATE auth.session AS s SET
          last_seen_utc  = @seenUtc
        , expires_utc    = @expiresUtc
      WHERE s.session_id = @sessionId;
      """;
    var parameters = new {
      sessionId,
      seenUtc,
      expiresUtc,
    };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }

  public async Task<bool> DeleteAsync(byte[] tokenHash, CancellationToken cancellationToken) {
    var sql = """
      DELETE FROM auth.session AS s
      WHERE s.token_hash = @tokenHash;
      """;
    return await _connection.ExecuteAsync(sql, new { tokenHash }, cancellationToken) > 0;
  }
}
