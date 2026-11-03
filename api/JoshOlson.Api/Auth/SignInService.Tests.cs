using Fido2NetLib;
using JoshOlson.Api.Common.Extensions;
using JoshOlson.Api.Common.Testing;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class SignInServiceTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly NpgsqlConnection _connection;
  private readonly FakeClock _clock;
  private readonly HttpClient _client;
  private readonly AuthTestData _authTestData;
  private readonly SessionService _sessionService;
  private readonly SignInService _signInService;
  private readonly SoftwareAuthenticator _authenticator;
  private int _userId;

  public SignInServiceTests(
    NpgsqlConnection connection,
    FakeClock clock,
    HttpClient client,
    AuthTestData authTestData,
    SessionService sessionService,
    SignInService signInService
  ) {
    _connection = connection;
    _clock = clock;
    _client = client;
    _authTestData = authTestData;
    _sessionService = sessionService;
    _signInService = signInService;
    _authenticator = authTestData.NewAuthenticator();
  }

  // A user with one passkey (`_authenticator`), registered through the API.
  public async ValueTask InitializeAsync()
    => _userId = await _authTestData.EnrollUserAsync(_client, _authenticator, _cancellationToken);

  public async ValueTask DisposeAsync() {
    _authenticator.Dispose();
    await _authTestData.DeleteUserAsync(_userId);
  }

  private SignInRequest NewRequest(CeremonyResponse<AssertionOptions> started)
    => new() {
      AttemptToken = started.AttemptToken,
      Response = _authenticator.Get(started.Options),
    };

  [Fact]
  public void Start_OffersNoCredentialIds() {
    var started = _signInService.Start();

    Assert.Empty(started.Options.AllowCredentials ?? []);
    Assert.NotEmpty(started.AttemptToken);
  }

  [Fact]
  public async Task Finish_ValidAssertion_StartsASession() {
    // The passkey was stored a moment ago by the database clock; the fake one must not be earlier than that.
    _clock.Advance(Duration.FromMinutes(1));
    var started = _signInService.Start();

    var token = await _signInService.FinishAsync(NewRequest(started), null, null, _cancellationToken);

    Assert.NotNull(token);
    Assert.Equal(_userId, (await _sessionService.AuthenticateAsync(token, _cancellationToken))?.User.UserId);
  }

  [Fact]
  public async Task Finish_UnknownAttemptToken_ReturnsNull() {
    var request = NewRequest(_signInService.Start()) with { AttemptToken = OpaqueToken.Create() };

    Assert.Null(await _signInService.FinishAsync(request, null, null, _cancellationToken));
  }

  [Fact]
  public async Task Finish_InactiveUser_ReturnsNull() {
    var request = NewRequest(_signInService.Start());
    var sql = """
      UPDATE auth.user AS u SET
          is_active = FALSE
      WHERE u.user_id = @userId;
      """;
    await _connection.ExecuteAsync(sql, new { userId = _userId }, _cancellationToken);

    Assert.Null(await _signInService.FinishAsync(request, null, null, _cancellationToken));
  }
}
