using JoshOlson.Api.Common.Testing;
using NodaTime;
using Xunit;

namespace JoshOlson.Api.Auth;

public class EnrollmentServiceTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly FakeClock _clock;
  private readonly AuthTestData _authTestData;
  private readonly EnrollmentService _enrollmentService;
  private int _userId;

  public EnrollmentServiceTests(FakeClock clock, AuthTestData authTestData, EnrollmentService enrollmentService) {
    _clock = clock;
    _authTestData = authTestData;
    _enrollmentService = enrollmentService;
  }

  public async ValueTask InitializeAsync()
    => _userId = await _authTestData.CreateUserAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _authTestData.DeleteUserAsync(_userId);

  [Fact]
  public async Task Start_ValidToken_OffersOptionsForThatUser() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);

    var started = await _enrollmentService.StartAsync(token, _cancellationToken);

    Assert.NotNull(started);
    Assert.Empty(started.Options.ExcludeCredentials);
    Assert.NotEmpty(started.Options.User.Id);
  }

  [Fact]
  public async Task Start_UnknownToken_ReturnsNull()
    => Assert.Null(await _enrollmentService.StartAsync(OpaqueToken.Create(), _cancellationToken));

  [Fact]
  public async Task Start_ExpiredToken_ReturnsNull() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);

    _clock.Advance(Duration.FromMinutes(6));

    Assert.Null(await _enrollmentService.StartAsync(token, _cancellationToken));
  }

  [Fact]
  public async Task Finish_AttemptTokenFromAnotherEnrollment_ReturnsNull() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);
    var other = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);
    var started = await _enrollmentService.StartAsync(token, _cancellationToken);
    using var authenticator = _authTestData.NewAuthenticator();
    var request = new EnrollmentRequest {
      Token = other,
      AttemptToken = started!.AttemptToken,
      Nickname = "foo",
      Response = authenticator.Create(started.Options),
    };

    Assert.Null(await _enrollmentService.FinishAsync(request, null, null, _cancellationToken));
  }
}
