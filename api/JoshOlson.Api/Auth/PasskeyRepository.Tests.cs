using System.Security.Cryptography;
using JoshOlson.Api.Common.Testing;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class PasskeyRepositoryTests {
  private static NewPasskey NewPasskey(int userId)
    => new() {
      UserId = userId,
      CredentialId = RandomNumberGenerator.GetBytes(32),
      PublicKey = RandomNumberGenerator.GetBytes(77),
      SignCount = 0,
      Transports = [PasskeyTransport.Usb, PasskeyTransport.Nfc],
      IsBackupEligible = false,
      IsBackedUp = false,
      Nickname = $"test-{Guid.NewGuid():N}",
    };

  [Fact]
  public async Task Lifecycle_CreatesReadsRecordsUseRenamesAndDeletes() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var passkeyRepository = new PasskeyRepository(connection);
    var userId = await AuthTestData.CreateUserAsync(connection, cancellationToken);
    try {
      var newPasskey = NewPasskey(userId);
      var passkeyId = await passkeyRepository.CreateAsync(newPasskey, cancellationToken);

      var created = await passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, cancellationToken);
      Assert.NotNull(created);
      Assert.Equal(passkeyId, created.PasskeyId);
      Assert.Equal(userId, created.UserId);
      Assert.Equal(newPasskey.PublicKey, created.PublicKey);
      Assert.Equal(newPasskey.Transports, created.Transports);
      Assert.Equal(newPasskey.Nickname, created.Nickname);
      Assert.Null(created.LastUsedUtc);
      var all = await passkeyRepository.GetAllAsync(userId, cancellationToken);
      Assert.Equal([passkeyId], all.Select(passkey => passkey.PasskeyId));

      var usedUtc = created.CreatedUtc + Duration.FromMinutes(5);
      Assert.True(await passkeyRepository.RecordUseAsync(passkeyId, 7, isBackedUp: false, usedUtc, cancellationToken));
      var used = await passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, cancellationToken);
      Assert.Equal(7, used?.SignCount);
      Assert.Equal(usedUtc, used?.LastUsedUtc);

      Assert.True(await passkeyRepository.RenameAsync(userId, passkeyId, "foo", cancellationToken));
      var renamed = await passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, cancellationToken);
      Assert.Equal("foo", renamed?.Nickname);

      Assert.True(await passkeyRepository.DeleteAsync(userId, passkeyId, cancellationToken));
      Assert.Null(await passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, cancellationToken));
      Assert.False(await passkeyRepository.DeleteAsync(userId, passkeyId, cancellationToken));
    } finally {
      await AuthTestData.DeleteUserAsync(connection, userId);
    }
  }

  [Fact]
  public async Task RenameAndDelete_AnotherUsersPasskey_ChangeNothing() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var passkeyRepository = new PasskeyRepository(connection);
    var ownerId = await AuthTestData.CreateUserAsync(connection, cancellationToken);
    var otherId = await AuthTestData.CreateUserAsync(connection, cancellationToken);
    try {
      var newPasskey = NewPasskey(ownerId);
      var passkeyId = await passkeyRepository.CreateAsync(newPasskey, cancellationToken);

      Assert.False(await passkeyRepository.RenameAsync(otherId, passkeyId, "foo", cancellationToken));
      Assert.False(await passkeyRepository.DeleteAsync(otherId, passkeyId, cancellationToken));
      var owned = await passkeyRepository.GetAllAsync(ownerId, cancellationToken);
      Assert.Equal(newPasskey.Nickname, owned.Single().Nickname);
      Assert.Empty(await passkeyRepository.GetAllAsync(otherId, cancellationToken));
    } finally {
      await AuthTestData.DeleteUserAsync(connection, ownerId);
      await AuthTestData.DeleteUserAsync(connection, otherId);
    }
  }

  [Fact]
  public async Task GetAll_OrdersByCreationThenKey() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var passkeyRepository = new PasskeyRepository(connection);
    var userId = await AuthTestData.CreateUserAsync(connection, cancellationToken);
    try {
      var first = await passkeyRepository.CreateAsync(NewPasskey(userId), cancellationToken);
      var second = await passkeyRepository.CreateAsync(NewPasskey(userId), cancellationToken);

      var all = await passkeyRepository.GetAllAsync(userId, cancellationToken);
      Assert.Equal([first, second], all.Select(passkey => passkey.PasskeyId));
    } finally {
      await AuthTestData.DeleteUserAsync(connection, userId);
    }
  }

  [Fact]
  public async Task Create_DuplicateCredentialIdOrNickname_ViolatesUniqueConstraint() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var passkeyRepository = new PasskeyRepository(connection);
    var userId = await AuthTestData.CreateUserAsync(connection, cancellationToken);
    try {
      var newPasskey = NewPasskey(userId);
      await passkeyRepository.CreateAsync(newPasskey, cancellationToken);

      var sameCredential = await Assert.ThrowsAsync<PostgresException>(
        () => passkeyRepository.CreateAsync(newPasskey with { Nickname = "foo" }, cancellationToken));
      Assert.Equal("uq_passkey_credential_id", sameCredential.ConstraintName);

      var otherPasskey = NewPasskey(userId) with { Nickname = newPasskey.Nickname };
      var sameNickname = await Assert.ThrowsAsync<PostgresException>(
        () => passkeyRepository.CreateAsync(otherPasskey, cancellationToken));
      Assert.Equal("uq_passkey_user_id_nickname", sameNickname.ConstraintName);
    } finally {
      await AuthTestData.DeleteUserAsync(connection, userId);
    }
  }

  [Fact]
  public async Task Create_BackedUpButNotEligible_ViolatesCheck() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var passkeyRepository = new PasskeyRepository(connection);
    var userId = await AuthTestData.CreateUserAsync(connection, cancellationToken);
    try {
      var exception = await Assert.ThrowsAsync<PostgresException>(
        () => passkeyRepository.CreateAsync(NewPasskey(userId) with { IsBackedUp = true }, cancellationToken));
      Assert.Equal("ck_passkey_backed_up_requires_eligible", exception.ConstraintName);
    } finally {
      await AuthTestData.DeleteUserAsync(connection, userId);
    }
  }
}
