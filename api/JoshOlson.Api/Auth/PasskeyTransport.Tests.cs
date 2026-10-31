using System.Security.Cryptography;
using JoshOlson.Api.Common.Testing;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class PasskeyTransportTests {
  // The enum is authoritative, but auth.passkey.transports has a range check that must list the same numbers.
  // Run in a transaction that is never committed, so nothing is left behind.
  [Fact]
  public async Task Values_MatchTheDatabaseRangeCheck() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
    var userId = await AuthTestData.CreateUserAsync(connection, cancellationToken);
    var passkeyRepository = new PasskeyRepository(connection);
    var all = Enum.GetValues<PasskeyTransport>();

    var newPasskey = NewPasskey(userId, all);
    await passkeyRepository.CreateAsync(newPasskey, cancellationToken);
    var stored = await passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, cancellationToken);
    Assert.Equal([1, 2, 3, 4, 5, 6], all.Select(transport => (int)transport));
    Assert.Equal(all, stored?.Transports);

    var next = all.Max() + 1;
    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => passkeyRepository.CreateAsync(NewPasskey(userId, [next]), cancellationToken));
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
