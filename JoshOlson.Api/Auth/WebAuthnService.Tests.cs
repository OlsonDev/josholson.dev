using Fido2NetLib.Objects;
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

  private static readonly Guid _ReportedAaguid = Guid.Parse("11111111-2222-3333-4444-555555555555");

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
  public async Task VerifyRegistration_ReportedAaguid_ReturnsTheCredential() {
    using var authenticator = NewAuthenticator();
    var options = _webAuthnService.CreateRegistrationOptions(_User, []);

    // Firefox reports the model's AAGUID with a security key even though attestation is 'none'.
    var attestation = authenticator.Create(options, _ReportedAaguid);
    var registration = await _webAuthnService.VerifyRegistrationAsync(attestation, options, _cancellationToken);

    Assert.Equal(authenticator.CredentialId, registration?.CredentialId);
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
