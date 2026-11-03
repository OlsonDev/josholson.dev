using System.Security.Cryptography;
using Xunit;

namespace JoshOlson.Api.Auth;

public class PasskeyServiceTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly AuthTestData _authTestData;
  private readonly AuthUserRepository _authUserRepository;
  private readonly PasskeyRepository _passkeyRepository;
  private readonly PasskeyService _passkeyService;
  private int _userId;
  private SessionPrincipal _principal = null!;

  public PasskeyServiceTests(
    AuthTestData authTestData,
    AuthUserRepository authUserRepository,
    PasskeyRepository passkeyRepository,
    PasskeyService passkeyService
  ) {
    _authTestData = authTestData;
    _authUserRepository = authUserRepository;
    _passkeyRepository = passkeyRepository;
    _passkeyService = passkeyService;
  }

  public async ValueTask InitializeAsync() {
    _userId = await _authTestData.CreateUserAsync(_cancellationToken);
    var user = await _authUserRepository.GetAsync(_userId, _cancellationToken);
    _principal = new(user!, SessionId: 1);
  }

  public async ValueTask DisposeAsync()
    => await _authTestData.DeleteUserAsync(_userId);

  private async Task<int> AddPasskeyAsync() {
    var passkey = new NewPasskey {
      UserId = _userId,
      CredentialId = RandomNumberGenerator.GetBytes(32),
      PublicKey = RandomNumberGenerator.GetBytes(77),
      SignCount = 0,
      Transports = [PasskeyTransport.Usb],
      IsBackupEligible = false,
      IsBackedUp = false,
      Nickname = $"test-{Guid.NewGuid():N}",
    };
    return await _passkeyRepository.CreateAsync(passkey, _cancellationToken);
  }

  [Fact]
  public async Task StartRemoval_UnknownPasskey_IsNotFound() {
    await AddPasskeyAsync();
    await AddPasskeyAsync();

    var (outcome, change) = await _passkeyService.StartRemovalAsync(_principal, 0, _cancellationToken);

    Assert.Equal(RemovalOutcome.NotFound, outcome);
    Assert.Null(change);
  }

  [Fact]
  public async Task StartRemoval_TheOnlyPasskey_IsRefused() {
    var only = await AddPasskeyAsync();

    var (outcome, change) = await _passkeyService.StartRemovalAsync(_principal, only, _cancellationToken);

    Assert.Equal(RemovalOutcome.LastPasskey, outcome);
    Assert.Null(change);
  }

  [Fact]
  public async Task StartRemoval_OneOfSeveral_HoldsTheChange() {
    var first = await AddPasskeyAsync();
    await AddPasskeyAsync();

    var (outcome, change) = await _passkeyService.StartRemovalAsync(_principal, first, _cancellationToken);

    Assert.Equal(RemovalOutcome.Started, outcome);
    Assert.NotNull(change);
    Assert.Equal(2, (await _passkeyRepository.GetAllAsync(_userId, _cancellationToken)).Count);
  }

  [Fact]
  public async Task StartConfirmation_ChangeHeldBy_AnotherSession_ReturnsNull() {
    var first = await AddPasskeyAsync();
    await AddPasskeyAsync();
    var (_, change) = await _passkeyService.StartRemovalAsync(_principal, first, _cancellationToken);

    var started = await _passkeyService.StartConfirmationAsync(
      _principal with { SessionId = 2 },
      change!.ChangeToken,
      _cancellationToken);

    Assert.Null(started);
  }

  [Fact]
  public async Task StartConfirmation_OffersEveryRegisteredPasskey() {
    var first = await AddPasskeyAsync();
    await AddPasskeyAsync();
    var (_, change) = await _passkeyService.StartRemovalAsync(_principal, first, _cancellationToken);

    var started = await _passkeyService.StartConfirmationAsync(_principal, change!.ChangeToken, _cancellationToken);

    Assert.Equal(2, started?.Options.AllowCredentials.Count);
  }

  [Fact]
  public async Task Confirm_UnknownAttemptToken_ReturnsFalse() {
    var request = new ConfirmationRequest {
      ChangeToken = OpaqueToken.Create(),
      AttemptToken = OpaqueToken.Create(),
      Response = new() {
        RawId = [],
        Response = new() {
          AuthenticatorData = [],
          ClientDataJson = [],
          Signature = [],
        },
      },
    };

    Assert.False(await _passkeyService.ConfirmAsync(_principal, request, _cancellationToken));
  }

  [Fact]
  public async Task Rename_ValidatesTheNicknameAgainstTheOtherPasskeys() {
    var first = await AddPasskeyAsync();
    var second = await AddPasskeyAsync();
    var nickname = (await _passkeyRepository.GetAllAsync(_userId, _cancellationToken))
      .Single(passkey => passkey.PasskeyId == second).Nickname;

    var taken = await _passkeyService.ValidateRenameAsync(
      _principal,
      first,
      new() { Nickname = nickname },
      _cancellationToken);
    var own = await _passkeyService.ValidateRenameAsync(
      _principal,
      second,
      new() { Nickname = nickname },
      _cancellationToken);

    Assert.True(taken.Has("nickname"));
    Assert.False(own.Any);
  }
}
