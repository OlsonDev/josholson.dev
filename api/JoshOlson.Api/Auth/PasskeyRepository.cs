using JoshOlson.Api.Common.Extensions;
using NodaTime;
using Npgsql;

namespace JoshOlson.Api.Auth;

public sealed class PasskeyRepository {
  private readonly NpgsqlConnection _connection;

  public PasskeyRepository(NpgsqlConnection connection)
    => _connection = connection;

  public async Task<IReadOnlyList<Passkey>> GetAllAsync(int userId, CancellationToken cancellationToken)
    => await GetAllByConditionAsync("p.user_id = @userId", new { userId }, cancellationToken);

  public async Task<Passkey?> GetByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken)
    => (await GetAllByConditionAsync("p.credential_id = @credentialId", new { credentialId }, cancellationToken))
      .SingleOrDefault();

  // The one place the select list lives. `onCondition` is SQL the code wrote, never request data; values go in
  // `parameters`. Ordered by creation then key, so a list is stable.
  private async Task<IReadOnlyList<Passkey>> GetAllByConditionAsync(
    string onCondition, object parameters, CancellationToken cancellationToken
  ) {
    var sql = $"""
      SELECT
          p.passkey_id          AS passkey_id
        , p.user_id             AS user_id
        , p.credential_id       AS credential_id
        , p.public_key          AS public_key
        , p.sign_count          AS sign_count
        , p.transports          AS transports
        , p.is_backup_eligible  AS is_backup_eligible
        , p.is_backed_up        AS is_backed_up
        , p.nickname            AS nickname
        , p.created_utc         AS created_utc
        , p.last_used_utc       AS last_used_utc
      FROM (SELECT)      AS _
      JOIN auth.passkey  AS p  ON {onCondition}
      ORDER BY p.created_utc, p.passkey_id;
      """;
    var rows = await _connection.QueryAsync<PasskeyRow>(sql, parameters, cancellationToken);
    return rows.Select(row => row.ToPasskey()).ToList();
  }

  public async Task<int> CreateAsync(NewPasskey passkey, CancellationToken cancellationToken) {
    var sql = """
      INSERT INTO auth.passkey (
          user_id
        , credential_id
        , public_key
        , sign_count
        , transports
        , is_backup_eligible
        , is_backed_up
        , nickname
      ) VALUES (
          @userId
        , @credentialId
        , @publicKey
        , @signCount
        , @transports
        , @isBackupEligible
        , @isBackedUp
        , @nickname
      )
      RETURNING passkey_id;
      """;
    var parameters = new {
      passkey.UserId,
      passkey.CredentialId,
      passkey.PublicKey,
      passkey.SignCount,
      Transports = passkey.Transports.Cast<short>().ToArray(),
      passkey.IsBackupEligible,
      passkey.IsBackedUp,
      passkey.Nickname,
    };
    return await _connection.QuerySingleAsync<int>(sql, parameters, cancellationToken);
  }

  // After a successful sign-in: the new counter, the sync state the authenticator reported, and when.
  public async Task<bool> RecordUseAsync(
    int passkeyId, long signCount, bool isBackedUp, Instant usedUtc, CancellationToken cancellationToken
  ) {
    var sql = """
      UPDATE auth.passkey AS p SET
          sign_count     = @signCount
        , is_backed_up   = @isBackedUp
        , last_used_utc  = @usedUtc
      WHERE p.passkey_id = @passkeyId;
      """;
    var parameters = new {
      passkeyId,
      signCount,
      isBackedUp,
      usedUtc,
    };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }

  public async Task<bool> RenameAsync(int userId, int passkeyId, string nickname, CancellationToken cancellationToken) {
    var sql = """
      UPDATE auth.passkey AS p SET
          nickname  = @nickname
      WHERE p.user_id     = @userId
        AND p.passkey_id  = @passkeyId;
      """;
    var parameters = new {
      userId,
      passkeyId,
      nickname,
    };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }

  public async Task<bool> DeleteAsync(int userId, int passkeyId, CancellationToken cancellationToken) {
    var sql = """
      DELETE FROM auth.passkey AS p
      WHERE p.user_id     = @userId
        AND p.passkey_id  = @passkeyId;
      """;
    var parameters = new {
      userId,
      passkeyId,
    };
    return await _connection.ExecuteAsync(sql, parameters, cancellationToken) > 0;
  }

  // Dapper cannot fill a PasskeyTransport list from SMALLINT[], so the row carries the raw numbers.
  private sealed record PasskeyRow {
    public required int PasskeyId { get; init; }
    public required int UserId { get; init; }
    public required byte[] CredentialId { get; init; }
    public required byte[] PublicKey { get; init; }
    public required long SignCount { get; init; }
    public required short[] Transports { get; init; }
    public required bool IsBackupEligible { get; init; }
    public required bool IsBackedUp { get; init; }
    public required string Nickname { get; init; }
    public required Instant CreatedUtc { get; init; }
    public required Instant? LastUsedUtc { get; init; }

    public Passkey ToPasskey()
      => new() {
        PasskeyId = PasskeyId,
        UserId = UserId,
        CredentialId = CredentialId,
        PublicKey = PublicKey,
        SignCount = SignCount,
        Transports = Transports.Cast<PasskeyTransport>().ToList(),
        IsBackupEligible = IsBackupEligible,
        IsBackedUp = IsBackedUp,
        Nickname = Nickname,
        CreatedUtc = CreatedUtc,
        LastUsedUtc = LastUsedUtc,
      };
  }
}
