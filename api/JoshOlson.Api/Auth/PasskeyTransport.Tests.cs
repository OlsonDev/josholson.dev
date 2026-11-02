using System.Security.Cryptography;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class PasskeyTransportTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly NpgsqlConnection _connection;
  private readonly AuthTestData _authTestData;
  private readonly PasskeyRepository _passkeyRepository;

  public PasskeyTransportTests(
    NpgsqlConnection connection,
    AuthTestData authTestData,
    PasskeyRepository passkeyRepository
  ) {
    _connection = connection;
    _authTestData = authTestData;
    _passkeyRepository = passkeyRepository;
  }

  // The enum is authoritative, but auth.passkey.transports has a range check that must list the same numbers.
  // Run in a transaction that is never committed, so nothing is left behind.
  [Fact]
  public async Task Values_MatchTheDatabaseRangeCheck() {
    await using var transaction = await _connection.BeginTransactionAsync(_cancellationToken);
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    var all = Enum.GetValues<PasskeyTransport>();

    var newPasskey = NewPasskey(userId, all);
    await _passkeyRepository.CreateAsync(newPasskey, _cancellationToken);
    var stored = await _passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, _cancellationToken);
    Assert.Equal([1, 2, 3, 4, 5, 6], all.Select(transport => (int)transport));
    Assert.Equal(all, stored?.Transports);

    var next = all.Max() + 1;
    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => _passkeyRepository.CreateAsync(NewPasskey(userId, [next]), _cancellationToken));
    Assert.Equal("ck_passkey_transports_range", exception.ConstraintName);
  }

  private static NewPasskey NewPasskey(int userId, IReadOnlyList<PasskeyTransport> transports)
    => new() {
      UserId = userId,
      CredentialId = RandomNumberGenerator.GetBytes(32),
      PublicKey = RandomNumberGenerator.GetBytes(77),
      SignCount = 0,
      Transports = transports,
      IsBackupEligible = false,
      IsBackedUp = false,
      Nickname = $"test-{Guid.NewGuid():N}",
    };
}
