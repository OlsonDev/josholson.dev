using Fido2NetLib;
using Fido2NetLib.Objects;

namespace JoshOlson.Api.Auth;

// The only code that talks to Fido2. It builds options and verifies responses; the callers own the state around them.
public sealed class WebAuthnService {
  private readonly IFido2 _fido2;
  private readonly PasskeyRepository _passkeyRepository;
  private readonly AaguidLookup _aaguidLookup;

  public WebAuthnService(IFido2 fido2, PasskeyRepository passkeyRepository, AaguidLookup aaguidLookup) {
    _fido2 = fido2;
    _passkeyRepository = passkeyRepository;
    _aaguidLookup = aaguidLookup;
  }

  // Discoverable credentials and user verification are required (ADR 0031, Red, Purple, or a phone). `existing` is
  // excluded, so one authenticator cannot be registered twice.
  public CredentialCreateOptions CreateRegistrationOptions(AuthUser user, List<Passkey> existing)
    => _fido2.RequestNewCredential(new RequestNewCredentialParams {
      User = new() {
        Id = user.UserHandle,
        Name = user.DisplayName,
        DisplayName = user.DisplayName,
      },
      ExcludeCredentials = ToDescriptors(existing),
      AuthenticatorSelection = new() {
        ResidentKey = ResidentKeyRequirement.Required,
        UserVerification = UserVerificationRequirement.Required,
      },
      AttestationPreference = AttestationConveyancePreference.None,
    });

  // An empty `allowed` list lets the browser offer any discoverable passkey (sign-in).
  public AssertionOptions CreateAssertionOptions(List<Passkey> allowed)
    => _fido2.GetAssertionOptions(new GetAssertionOptionsParams {
      AllowedCredentials = ToDescriptors(allowed),
      UserVerification = UserVerificationRequirement.Required,
    });

  // Null when the response does not verify. Throws UnexpectedAaguidException if the authenticator reports an AAGUID.
  public async Task<VerifiedRegistration?> VerifyRegistrationAsync(
    AuthenticatorAttestationRawResponse response,
    CredentialCreateOptions options,
    CancellationToken cancellationToken
  ) {
    RegisteredPublicKeyCredential credential;
    try {
      credential = await _fido2.MakeNewCredentialAsync(
        new MakeNewCredentialParams {
          AttestationResponse = response,
          OriginalOptions = options,
          IsCredentialIdUniqueToUserCallback = async (args, token)
            => await _passkeyRepository.GetByCredentialIdAsync(args.CredentialId, token) is null,
        },
        cancellationToken);
    } catch (Fido2VerificationException) {
      return null;
    }

    if (credential.AaGuid != Guid.Empty) {
      var modelName = await _aaguidLookup.FindNameAsync(credential.AaGuid, cancellationToken);
      throw new UnexpectedAaguidException(credential.AaGuid, modelName);
    }
    var reported = credential.Transports ?? [];
    var transports = new List<PasskeyTransport>(reported.Length);
    foreach (var transport in reported) {
      if (ToTransport(transport) is { } known) transports.Add(known);
    }
    return new() {
      CredentialId = credential.Id,
      PublicKey = credential.PublicKey,
      SignCount = credential.SignCount,
      Transports = transports,
      IsBackupEligible = credential.IsBackupEligible,
      IsBackedUp = credential.IsBackedUp,
    };
  }

  // Null when the response does not verify, or when the passkey does not belong to the user.
  public async Task<VerifyAssertionResult?> VerifyAssertionAsync(
    AuthenticatorAssertionRawResponse response,
    AssertionOptions options,
    Passkey passkey,
    AuthUser user,
    CancellationToken cancellationToken
  ) {
    if (passkey.UserId != user.UserId) return null;
    try {
      return await _fido2.MakeAssertionAsync(
        new MakeAssertionParams {
          AssertionResponse = response,
          OriginalOptions = options,
          StoredPublicKey = passkey.PublicKey,
          StoredSignatureCounter = (uint)passkey.SignCount,
          IsUserHandleOwnerOfCredentialIdCallback = (args, _)
            => Task.FromResult(OwnsHandle(args.UserHandle, user)),
        },
        cancellationToken);
    } catch (Fido2VerificationException) {
      return null;
    }
  }

  // A discoverable credential reports the handle of its owner; it has to be the user the passkey belongs to.
  private static bool OwnsHandle(byte[]? userHandle, AuthUser user)
    => userHandle is { Length: > 0 } && userHandle.AsSpan().SequenceEqual(user.UserHandle);

  private static List<PublicKeyCredentialDescriptor> ToDescriptors(List<Passkey> passkeys) {
    var descriptors = new List<PublicKeyCredentialDescriptor>(passkeys.Count);
    foreach (var passkey in passkeys) {
      var transports = new AuthenticatorTransport[passkey.Transports.Count];
      for (var i = 0; i < transports.Length; i++) transports[i] = ToAuthenticatorTransport(passkey.Transports[i]);
      descriptors.Add(new(PublicKeyCredentialType.PublicKey, passkey.CredentialId, transports));
    }
    return descriptors;
  }

  private static AuthenticatorTransport ToAuthenticatorTransport(PasskeyTransport transport)
    => transport switch {
      PasskeyTransport.Usb => AuthenticatorTransport.Usb,
      PasskeyTransport.Nfc => AuthenticatorTransport.Nfc,
      PasskeyTransport.Ble => AuthenticatorTransport.Ble,
      PasskeyTransport.Hybrid => AuthenticatorTransport.Hybrid,
      PasskeyTransport.Internal => AuthenticatorTransport.Internal,
      PasskeyTransport.SmartCard => AuthenticatorTransport.SmartCard,
      _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
    };

  // A transport this site has no number for (a newer browser may report one) is dropped, not stored.
  private static PasskeyTransport? ToTransport(AuthenticatorTransport transport)
    => transport switch {
      AuthenticatorTransport.Usb => PasskeyTransport.Usb,
      AuthenticatorTransport.Nfc => PasskeyTransport.Nfc,
      AuthenticatorTransport.Ble => PasskeyTransport.Ble,
      AuthenticatorTransport.Hybrid => PasskeyTransport.Hybrid,
      AuthenticatorTransport.Internal => PasskeyTransport.Internal,
      AuthenticatorTransport.SmartCard => PasskeyTransport.SmartCard,
      _ => null,
    };
}
