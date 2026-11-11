using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Api.Http;

// A user agent is stored once and referenced by ID (`http.user_agent`), since browsers repeat the same string on
// every request.
public sealed class UserAgentRepository {
  // The longest value `http.user_agent` accepts (`ck_user_agent_value_length`).
  public const int MaxLength = 512;

  private readonly NpgsqlConnection _connection;

  public UserAgentRepository(NpgsqlConnection connection)
    => _connection = connection;

  // The ID of the user agent, adding it first if it is new. `userAgent` must already be trimmed and at most
  // `MaxLength`.
  public async Task<int> GetOrCreateIdAsync(string userAgent, CancellationToken cancellationToken) {
    var selectSql = """
      SELECT
          ua.user_agent_id  AS user_agent_id
      FROM (SELECT)         AS _
      JOIN http.user_agent  AS ua  ON ua.value = @userAgent;
      """;
    var parameters = new { userAgent };
    var existingId = await _connection.QuerySingleOrDefaultAsync<int?>(selectSql, parameters, cancellationToken);
    if (existingId is { } id) return id;

    // Two requests can both miss the select. The conflict update changes nothing; it only lets `RETURNING` hand the
    // winner's ID to the loser.
    var insertSql = """
      INSERT INTO http.user_agent AS ua (
          value
      ) VALUES (
          @userAgent
      )
      ON CONFLICT (value) DO UPDATE SET
          value = EXCLUDED.value
      RETURNING user_agent_id;
      """;
    return await _connection.QuerySingleAsync<int>(insertSql, parameters, cancellationToken);
  }
}
