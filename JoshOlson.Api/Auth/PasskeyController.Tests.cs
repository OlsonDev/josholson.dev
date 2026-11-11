using System.Globalization;
using System.Net;
using Fido2NetLib;
using Xunit;

namespace JoshOlson.Api.Auth;

public class PasskeyControllerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly HttpClient _client;
  private readonly AuthTestData _authTestData;
  private readonly SoftwareAuthenticator _authenticator;
  private int _userId;

  public PasskeyControllerTests(HttpClient client, AuthTestData authTestData) {
    _client = client;
    _authTestData = authTestData;
    _authenticator = authTestData.NewAuthenticator();
  }

  // `_client` is signed in as a user with one passkey (`_authenticator`, nicknamed "first").
  public async ValueTask InitializeAsync()
    => _userId = await _authTestData.EnrollUserAsync(_client, _authenticator, _cancellationToken);

  public async ValueTask DisposeAsync() {
    _authenticator.Dispose();
    await _authTestData.DeleteUserAsync(_userId);
  }

  private static string PasskeyUrl(int passkeyId, string suffix = "")
    => string.Create(CultureInfo.InvariantCulture, $"/auth/passkeys/{passkeyId}{suffix}");

  private Task<List<PasskeyResponse>> ListAsync()
    => AuthTestData.ListPasskeysAsync(_client, _cancellationToken);

  // The two ceremonies of an addition: register the authenticator (held, not stored), then confirm with another.
  private async Task<string> HoldAdditionAsync(SoftwareAuthenticator added, string nickname) {
    var optionsResponse = await _client.PostAsync("/auth/passkeys/registration/options", content: null, _cancellationToken);
    var started = await optionsResponse.Content
      .ReadFromJsonAsync<CeremonyResponse<CredentialCreateOptions>>(_cancellationToken);
    var request = new RegistrationRequest {
      AttemptToken = started!.AttemptToken,
      Nickname = nickname,
      Response = added.Create(started.Options),
    };
    var response = await _client.PostAsJsonAsync("/auth/passkeys/registration", request, _cancellationToken);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<PendingChangeResponse>(_cancellationToken))!.ChangeToken;
  }

  private async Task<HttpResponseMessage> ConfirmAsync(string changeToken, SoftwareAuthenticator confirmer) {
    var optionsResponse = await _client.PostAsJsonAsync(
      "/auth/passkeys/confirmation/options",
      new { changeToken },
      _cancellationToken);
    Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
    var started = await optionsResponse.Content
      .ReadFromJsonAsync<CeremonyResponse<AssertionOptions>>(_cancellationToken);
    var request = new ConfirmationRequest {
      ChangeToken = changeToken,
      AttemptToken = started!.AttemptToken,
      Response = confirmer.Get(started.Options),
    };
    return await _client.PostAsJsonAsync("/auth/passkeys/confirmation", request, _cancellationToken);
  }

  [Fact]
  public async Task Add_ConfirmedByARegisteredPasskey_IsStored() {
    using var added = _authTestData.NewAuthenticator();

    var changeToken = await HoldAdditionAsync(added, "second");
    Assert.Single(await ListAsync());

    var response = await ConfirmAsync(changeToken, _authenticator);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    var passkeys = await ListAsync();
    Assert.Equal(["first", "second"], passkeys.Select(passkey => passkey.Nickname));
  }

  [Fact]
  public async Task Add_ConfirmedByTheNewCredentialItself_IsRefused() {
    using var added = _authTestData.NewAuthenticator();
    var changeToken = await HoldAdditionAsync(added, "second");

    var response = await ConfirmAsync(changeToken, added);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Single(await ListAsync());
  }

  [Fact]
  public async Task Add_SameAuthenticatorTwice_IsRefused() {
    var optionsResponse = await _client.PostAsync("/auth/passkeys/registration/options", content: null, _cancellationToken);
    var started = await optionsResponse.Content
      .ReadFromJsonAsync<CeremonyResponse<CredentialCreateOptions>>(_cancellationToken);

    Assert.Equal(_authenticator.CredentialId, started!.Options.ExcludeCredentials.Single().Id);
  }

  [Fact]
  public async Task Remove_ConfirmedByAnotherPasskey_DeletesIt() {
    using var added = _authTestData.NewAuthenticator();
    var addition = await HoldAdditionAsync(added, "second");
    var additionResponse = await ConfirmAsync(addition, _authenticator);
    Assert.Equal(HttpStatusCode.NoContent, additionResponse.StatusCode);
    var passkeys = await ListAsync();
    var second = passkeys.Single(passkey => string.Equals(passkey.Nickname, "second", StringComparison.Ordinal));

    var removal = await _client.PostAsync(PasskeyUrl(second.PasskeyId, "/removal"), content: null, _cancellationToken);
    var pending = await removal.Content.ReadFromJsonAsync<PendingChangeResponse>(_cancellationToken);
    var changeToken = pending!.ChangeToken;
    Assert.Equal(2, (await ListAsync()).Count);
    var response = await ConfirmAsync(changeToken, _authenticator);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal("first", Assert.Single(await ListAsync()).Nickname);
  }

  [Fact]
  public async Task Remove_TheLastPasskey_ReturnsConflict() {
    var only = Assert.Single(await ListAsync());

    var response = await _client.PostAsync(PasskeyUrl(only.PasskeyId, "/removal"), content: null, _cancellationToken);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task Rename_ChangesTheNickname() {
    var only = Assert.Single(await ListAsync());

    var url = PasskeyUrl(only.PasskeyId);
    var response = await _client.PutAsJsonAsync(url, new { nickname = "renamed" }, _cancellationToken);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal("renamed", Assert.Single(await ListAsync()).Nickname);
  }

  [Fact]
  public async Task GetAll_WithoutASession_ReturnsUnauthorized() {
    using var browser = _authTestData.NewBrowser();

    var response = await browser.GetAsync("/auth/passkeys", _cancellationToken);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }
}
