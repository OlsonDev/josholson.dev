using System.Net;
using Fido2NetLib;
using Xunit;

namespace JoshOlson.Api.Auth;

public class SignInControllerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly HttpClient _client;
  private readonly AuthTestData _authTestData;
  private readonly SoftwareAuthenticator _authenticator;
  private int _userId;

  public SignInControllerTests(HttpClient client, AuthTestData authTestData) {
    _client = client;
    _authTestData = authTestData;
    _authenticator = authTestData.NewAuthenticator();
  }

  // `_client` enrolls the user, which also signs it in. A test that needs a browser that is not signed in asks for
  // another.
  public async ValueTask InitializeAsync()
    => _userId = await _authTestData.EnrollUserAsync(_client, _authenticator, _cancellationToken);

  public async ValueTask DisposeAsync() {
    _authenticator.Dispose();
    await _authTestData.DeleteUserAsync(_userId);
  }

  private async Task<HttpResponseMessage> SignInAsync(HttpClient browser, SoftwareAuthenticator authenticator) {
    var optionsResponse = await browser.PostAsync("/auth/sign-in/options", content: null, _cancellationToken);
    var started = await optionsResponse.Content
      .ReadFromJsonAsync<CeremonyResponse<AssertionOptions>>(_cancellationToken);
    Assert.Empty(started!.Options.AllowCredentials ?? []);
    var request = new SignInRequest {
      AttemptToken = started.AttemptToken,
      Response = authenticator.Get(started.Options),
    };
    return await browser.PostAsJsonAsync("/auth/sign-in", request, _cancellationToken);
  }

  [Fact]
  public async Task SignIn_RegisteredPasskey_StartsASession() {
    using var browser = _authTestData.NewBrowser();

    var response = await SignInAsync(browser, _authenticator);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    var me = await browser.GetFromJsonAsync<CurrentUserResponse>("/auth/me", _cancellationToken);
    Assert.Equal(_userId, me?.UserId);
    var passkeys = await AuthTestData.ListPasskeysAsync(browser, _cancellationToken);
    Assert.NotNull(Assert.Single(passkeys).LastUsedUtc);
  }

  [Fact]
  public async Task SignIn_UnregisteredAuthenticator_ReturnsUnauthorized() {
    using var stranger = _authTestData.NewAuthenticator();
    using var browser = _authTestData.NewBrowser();

    var response = await SignInAsync(browser, stranger);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task SignIn_ChallengeUsedTwice_ReturnsUnauthorized() {
    using var browser = _authTestData.NewBrowser();
    var optionsResponse = await browser.PostAsync("/auth/sign-in/options", content: null, _cancellationToken);
    var started = await optionsResponse.Content
      .ReadFromJsonAsync<CeremonyResponse<AssertionOptions>>(_cancellationToken);
    var request = new SignInRequest {
      AttemptToken = started!.AttemptToken,
      Response = _authenticator.Get(started.Options),
    };

    var first = await browser.PostAsJsonAsync("/auth/sign-in", request, _cancellationToken);
    var replay = await browser.PostAsJsonAsync("/auth/sign-in", request, _cancellationToken);
    Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
  }

  [Fact]
  public async Task Me_WithoutASession_ReturnsUnauthorized() {
    using var browser = _authTestData.NewBrowser();

    var response = await browser.GetAsync("/auth/me", _cancellationToken);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task SignOut_EndsTheSession() {
    var response = await _client.PostAsync("/auth/sign-out", content: null, _cancellationToken);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/auth/me", _cancellationToken)).StatusCode);
  }
}
