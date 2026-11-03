using System.Net;
using Xunit;

namespace JoshOlson.Api.Auth;

public class EnrollmentControllerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly HttpClient _client;
  private readonly AuthTestData _authTestData;
  private readonly SoftwareAuthenticator _authenticator;
  private int _userId;

  public EnrollmentControllerTests(HttpClient client, AuthTestData authTestData) {
    _client = client;
    _authTestData = authTestData;
    _authenticator = authTestData.NewAuthenticator();
  }

  public async ValueTask InitializeAsync()
    => _userId = await _authTestData.EnrollUserAsync(_client, _authenticator, _cancellationToken);

  public async ValueTask DisposeAsync() {
    _authenticator.Dispose();
    await _authTestData.DeleteUserAsync(_userId);
  }

  [Fact]
  public async Task Enroll_ValidToken_StoresThePasskeyAndSignsIn() {
    var me = await _client.GetFromJsonAsync<CurrentUserResponse>("/auth/me", _cancellationToken);
    Assert.Equal(_userId, me?.UserId);
    var passkeys = await AuthTestData.ListPasskeysAsync(_client, _cancellationToken);
    var passkey = Assert.Single(passkeys);
    Assert.Equal("first", passkey.Nickname);
    Assert.Equal([PasskeyTransport.Usb], passkey.Transports);
  }

  [Fact]
  public async Task Enroll_UsedToken_IsRefused() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);
    using var second = _authTestData.NewAuthenticator();
    using var anonymous = _authTestData.NewBrowser();

    var first = await AuthTestData.EnrollAsync(anonymous, token, second, "second", _cancellationToken);
    Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

    var replay = await anonymous.PostAsJsonAsync("/auth/enrollment/options", new { token }, _cancellationToken);
    Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
  }

  [Fact]
  public async Task Options_UnknownToken_ReturnsNotFound() {
    using var anonymous = _authTestData.NewBrowser();

    var token = OpaqueToken.Create();
    var response = await anonymous.PostAsJsonAsync("/auth/enrollment/options", new { token }, _cancellationToken);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Enroll_DuplicateNickname_ReturnsUnprocessable() {
    var token = await _authTestData.CreateEnrollmentTokenAsync(_userId, _cancellationToken);
    using var second = _authTestData.NewAuthenticator();
    using var anonymous = _authTestData.NewBrowser();

    var response = await AuthTestData.EnrollAsync(anonymous, token, second, "first", _cancellationToken);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
  }
}
