using System.Security.Cryptography;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class PasskeyRepositoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly AuthTestData _authTestData;
  private readonly PasskeyRepository _passkeyRepository;

  public PasskeyRepositoryTests(AuthTestData authTestData, PasskeyRepository passkeyRepository) {
    _authTestData = authTestData;
    _passkeyRepository = passkeyRepository;
  }

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
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var newPasskey = NewPasskey(userId);
      var passkeyId = await _passkeyRepository.CreateAsync(newPasskey, _cancellationToken);

      var created = await _passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, _cancellationToken);
      Assert.NotNull(created);
      Assert.Equal(passkeyId, created.PasskeyId);
      Assert.Equal(userId, created.UserId);
      Assert.Equal(newPasskey.PublicKey, created.PublicKey);
      Assert.Equal(newPasskey.Transports, created.Transports);
      Assert.Equal(newPasskey.Nickname, created.Nickname);
      Assert.Null(created.LastUsedUtc);
      var all = await _passkeyRepository.GetAllAsync(userId, _cancellationToken);
      Assert.Equal([passkeyId], all.Select(passkey => passkey.PasskeyId));

      var usedUtc = created.CreatedUtc + Duration.FromMinutes(5);
      Assert.True(await _passkeyRepository.RecordUseAsync(passkeyId, 7, isBackedUp: false, usedUtc, _cancellationToken));
      var used = await _passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, _cancellationToken);
      Assert.Equal(7, used?.SignCount);
      Assert.Equal(usedUtc, used?.LastUsedUtc);

      Assert.True(await _passkeyRepository.RenameAsync(userId, passkeyId, "foo", _cancellationToken));
      var renamed = await _passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, _cancellationToken);
      Assert.Equal("foo", renamed?.Nickname);

      Assert.True(await _passkeyRepository.DeleteAsync(userId, passkeyId, _cancellationToken));
      Assert.Null(await _passkeyRepository.GetByCredentialIdAsync(newPasskey.CredentialId, _cancellationToken));
      Assert.False(await _passkeyRepository.DeleteAsync(userId, passkeyId, _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task RenameAndDelete_AnotherUsersPasskey_ChangeNothing() {
    var ownerId = await _authTestData.CreateUserAsync(_cancellationToken);
    var otherId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var newPasskey = NewPasskey(ownerId);
      var passkeyId = await _passkeyRepository.CreateAsync(newPasskey, _cancellationToken);

      Assert.False(await _passkeyRepository.RenameAsync(otherId, passkeyId, "foo", _cancellationToken));
      Assert.False(await _passkeyRepository.DeleteAsync(otherId, passkeyId, _cancellationToken));
      var owned = await _passkeyRepository.GetAllAsync(ownerId, _cancellationToken);
      Assert.Equal(newPasskey.Nickname, owned.Single().Nickname);
      Assert.Empty(await _passkeyRepository.GetAllAsync(otherId, _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(ownerId);
      await _authTestData.DeleteUserAsync(otherId);
    }
  }

  [Fact]
  public async Task GetAll_OrdersByCreationThenKey() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var first = await _passkeyRepository.CreateAsync(NewPasskey(userId), _cancellationToken);
      var second = await _passkeyRepository.CreateAsync(NewPasskey(userId), _cancellationToken);

      var all = await _passkeyRepository.GetAllAsync(userId, _cancellationToken);
      Assert.Equal([first, second], all.Select(passkey => passkey.PasskeyId));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task Create_DuplicateCredentialIdOrNickname_ViolatesUniqueConstraint() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var newPasskey = NewPasskey(userId);
      await _passkeyRepository.CreateAsync(newPasskey, _cancellationToken);

      var sameCredential = await Assert.ThrowsAsync<PostgresException>(
        () => _passkeyRepository.CreateAsync(newPasskey with { Nickname = "foo" }, _cancellationToken));
      Assert.Equal("uq_passkey_credential_id", sameCredential.ConstraintName);

      var otherPasskey = NewPasskey(userId) with { Nickname = newPasskey.Nickname };
      var sameNickname = await Assert.ThrowsAsync<PostgresException>(
        () => _passkeyRepository.CreateAsync(otherPasskey, _cancellationToken));
      Assert.Equal("uq_passkey_user_id_nickname", sameNickname.ConstraintName);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task Create_BackedUpButNotEligible_ViolatesCheck() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var exception = await Assert.ThrowsAsync<PostgresException>(
        () => _passkeyRepository.CreateAsync(NewPasskey(userId) with { IsBackedUp = true }, _cancellationToken));
      Assert.Equal("ck_passkey_backed_up_requires_eligible", exception.ConstraintName);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }
}
