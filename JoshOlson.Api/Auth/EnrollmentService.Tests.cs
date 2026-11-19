using System.Security.Cryptography;
using JoshOlson.Api.Common.Testing;
using NodaTime;
using Xunit;

namespace JoshOlson.Api.Auth;

public class EnrollmentServiceTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly FakeClock _clock;
  private readonly AuthTestData _authTestData;
  private readonly EnrollmentService _enrollmentService;
  private readonly PasskeyRepository _passkeyRepository;
  private int _userId;

  public EnrollmentServiceTests(
    FakeClock clock,
    AuthTestData authTestData,
    EnrollmentService enrollmentService,
    PasskeyRepository passkeyRepository
  ) {
    _clock = clock;
    _authTestData = authTestData;
    _enrollmentService = enrollmentService;
    _passkeyRepository = passkeyRepository;
  }

  public async ValueTask InitializeAsync()
    => _userId = await _authTestData.CreateUserAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _authTestData.DeleteUserAsync(_userId);

  [Fact]
  public async Task Start_ValidToken_OffersOptionsForThatUser() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);

    var started = await _enrollmentService.StartAsync(Start(token), _cancellationToken);

    Assert.NotNull(started);
    Assert.Empty(started.Options.ExcludeCredentials);
    Assert.NotEmpty(started.Options.User.Id);
    Assert.EndsWith(" (foo)", started.Options.User.Name, StringComparison.Ordinal);
  }

  private static EnrollmentOptionsRequest Start(string token, string nickname = "foo")
    => new() {
      Token = token,
      Nickname = nickname,
    };

  [Fact]
  public async Task ValidateStart_ANicknameAnotherPasskeyHas_IsRefused() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);
    await _passkeyRepository.CreateAsync(NewPasskey("taken"), _cancellationToken);

    var errors = await _enrollmentService.ValidateStartAsync(Start(token, "taken"), _cancellationToken);

    Assert.True(errors.Has("nickname"));
    Assert.False((await _enrollmentService.ValidateStartAsync(Start(token), _cancellationToken)).Any);
  }

  private NewPasskey NewPasskey(string nickname)
    => new() {
      UserId = _userId,
      CredentialId = RandomNumberGenerator.GetBytes(32),
      PublicKey = RandomNumberGenerator.GetBytes(77),
      SignCount = 0,
      Transports = [PasskeyTransport.Usb],
      IsBackupEligible = false,
      IsBackedUp = false,
      Nickname = nickname,
    };

  [Fact]
  public async Task Start_UnknownToken_ReturnsNull()
    => Assert.Null(await _enrollmentService.StartAsync(Start(OpaqueToken.Create()), _cancellationToken));

  [Fact]
  public async Task Start_ExpiredToken_ReturnsNull() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);

    _clock.Advance(Duration.FromMinutes(6));

    Assert.Null(await _enrollmentService.StartAsync(Start(token), _cancellationToken));
  }

  [Fact]
  public async Task Finish_AttemptTokenFromAnotherEnrollment_ReturnsNull() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);
    var other = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);
    var started = await _enrollmentService.StartAsync(Start(token), _cancellationToken);
    using var authenticator = _authTestData.NewAuthenticator();
    var request = new EnrollmentRequest {
      Token = other,
      AttemptToken = started!.AttemptToken,
      Response = authenticator.Create(started.Options),
    };

    Assert.Null(await _enrollmentService.FinishAsync(request, ip: null, userAgent: null, _cancellationToken));
  }
}
