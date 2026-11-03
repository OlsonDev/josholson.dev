using Fido2NetLib.Objects;
using Flurl.Http.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace JoshOlson.Api.Auth;

public class WebAuthnServiceTests {
  private static readonly AuthUser _User = new() {
    UserId = 1,
    DisplayName = "foo",
    UserHandle = AuthTestData.NewHandle(),
    IsActive = true,
  };

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly AuthOptions _options;
  private readonly WebAuthnService _webAuthnService;

  public WebAuthnServiceTests(IOptions<AuthOptions> options, WebAuthnService webAuthnService) {
    _options = options.Value;
    _webAuthnService = webAuthnService;
  }

  // An authenticator that acts from the origin the service accepts, unless a test says otherwise.
  private SoftwareAuthenticator NewAuthenticator(string? origin = null)
    => new(origin ?? _options.AllowedOrigin, _options.RelyingPartyId);

  [Fact]
  public async Task VerifyRegistration_AllZeroAaguid_ReturnsTheCredential() {
    using var authenticator = NewAuthenticator();
    var options = _webAuthnService.CreateRegistrationOptions(_User, []);

    var attestation = authenticator.Create(options);
    var registration = await _webAuthnService.VerifyRegistrationAsync(attestation, options, _cancellationToken);

    Assert.Equal(authenticator.CredentialId, registration?.CredentialId);
    Assert.Equal([PasskeyTransport.Usb], registration?.Transports);
  }

  [Fact]
  public async Task VerifyRegistration_ReportedAaguid_ThrowsNamingTheModel() {
    using var httpTest = new HttpTest();
    var models = new Dictionary<string, object> { [AaguidLookupTests.Aaguid.ToString()] = new { name = "Foo Key" } };
    httpTest.RespondWithJson(models);
    using var authenticator = NewAuthenticator();
    var options = _webAuthnService.CreateRegistrationOptions(_User, []);

    var attestation = authenticator.Create(options, AaguidLookupTests.Aaguid);
    var exception = await Assert.ThrowsAsync<UnexpectedAaguidException>(
      () => _webAuthnService.VerifyRegistrationAsync(attestation, options, _cancellationToken));

    Assert.Contains(AaguidLookupTests.Aaguid.ToString(), exception.Message, StringComparison.Ordinal);
    Assert.Contains("Foo Key", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task VerifyRegistration_ReportedAaguidWithFailedLookup_StillThrows() {
    using var httpTest = new HttpTest();
    httpTest.RespondWith("oops", 500);
    using var authenticator = NewAuthenticator();
    var options = _webAuthnService.CreateRegistrationOptions(_User, []);

    var attestation = authenticator.Create(options, AaguidLookupTests.Aaguid);
    var exception = await Assert.ThrowsAsync<UnexpectedAaguidException>(
      () => _webAuthnService.VerifyRegistrationAsync(attestation, options, _cancellationToken));

    Assert.Contains("model unknown", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task VerifyRegistration_AnotherOrigin_ReturnsNull() {
    using var authenticator = NewAuthenticator("https://evil.example");
    var options = _webAuthnService.CreateRegistrationOptions(_User, []);

    var attestation = authenticator.Create(options);
    Assert.Null(await _webAuthnService.VerifyRegistrationAsync(attestation, options, _cancellationToken));
  }

  [Fact]
  public void CreateOptions_RequireDiscoverableCredentialsAndUserVerification() {
    var registration = _webAuthnService.CreateRegistrationOptions(_User, []);
    var assertion = _webAuthnService.CreateAssertionOptions([]);

    Assert.Equal(ResidentKeyRequirement.Required, registration.AuthenticatorSelection?.ResidentKey);
    Assert.Equal(UserVerificationRequirement.Required, registration.AuthenticatorSelection?.UserVerification);
    Assert.Equal(AttestationConveyancePreference.None, registration.Attestation);
    Assert.Equal(UserVerificationRequirement.Required, assertion.UserVerification);
    Assert.Empty(assertion.AllowCredentials ?? []);
  }
}
