using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.WebUtilities;

namespace JoshOlson.Api.Auth;

// A stand-in for a security key or a phone: it makes `none` attestations and ES256 assertions the way a browser hands
// them over, so the tests cover the real verification path without hardware.
public sealed class SoftwareAuthenticator : IDisposable {
  private const byte _UserPresent = 0x01;
  private const byte _UserVerified = 0x04;
  private const byte _AttestedCredentialData = 0x40;

  private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  private readonly string _origin;
  private readonly string _rpId;
  private uint _signCount;

  public SoftwareAuthenticator(string origin, string rpId) {
    _origin = origin;
    _rpId = rpId;
  }

  public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

  // Set by Create, and returned with every assertion the way a discoverable credential does.
  public byte[]? UserHandle { get; private set; }

  public AuthenticatorAttestationRawResponse Create(CredentialCreateOptions options, Guid? aaguid = null) {
    UserHandle = options.User.Id;
    var clientData = ClientData("webauthn.create", options.Challenge);
    var authData = AuthenticatorData(
      _UserPresent | _UserVerified | _AttestedCredentialData,
      aaguid ?? Guid.Empty);
    var attestationObject = new CborWriter(CborConformanceMode.Ctap2Canonical);
    attestationObject.WriteStartMap(3);
    attestationObject.WriteTextString("fmt");
    attestationObject.WriteTextString("none");
    attestationObject.WriteTextString("attStmt");
    attestationObject.WriteStartMap(0);
    attestationObject.WriteEndMap();
    attestationObject.WriteTextString("authData");
    attestationObject.WriteByteString(authData);
    attestationObject.WriteEndMap();

    return new() {
      Id = WebEncoders.Base64UrlEncode(CredentialId),
      RawId = CredentialId,
      Type = PublicKeyCredentialType.PublicKey,
      ClientExtensionResults = new(),
      Response = new() {
        AttestationObject = attestationObject.Encode(),
        ClientDataJson = clientData,
        Transports = [AuthenticatorTransport.Usb],
      },
    };
  }

  public AuthenticatorAssertionRawResponse Get(AssertionOptions options) {
    var clientData = ClientData("webauthn.get", options.Challenge);
    var authData = AuthenticatorData(_UserPresent | _UserVerified, aaguid: null);
    var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
    return new() {
      Id = WebEncoders.Base64UrlEncode(CredentialId),
      RawId = CredentialId,
      Type = PublicKeyCredentialType.PublicKey,
      ClientExtensionResults = new(),
      Response = new() {
        AuthenticatorData = authData,
        ClientDataJson = clientData,
        Signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
        UserHandle = UserHandle,
      },
    };
  }

  public void Dispose()
    => _key.Dispose();

  private byte[] ClientData(string type, byte[] challenge)
    => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> {
      ["type"] = type,
      ["challenge"] = WebEncoders.Base64UrlEncode(challenge),
      ["origin"] = _origin,
      ["crossOrigin"] = false,
    });

  // The attested credential data is included when `aaguid` is given (registration).
  private byte[] AuthenticatorData(byte flags, Guid? aaguid) {
    _signCount++;
    var data = new List<byte>();
    data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(_rpId)));
    data.Add(flags);
    data.AddRange([(byte)(_signCount >> 24), (byte)(_signCount >> 16), (byte)(_signCount >> 8), (byte)_signCount]);
    if (aaguid is null) return [.. data];

    data.AddRange(aaguid.Value.ToByteArray(bigEndian: true));
    data.AddRange([(byte)(CredentialId.Length >> 8), (byte)CredentialId.Length]);
    data.AddRange(CredentialId);
    data.AddRange(CoseKey());
    return [.. data];
  }

  // The public key as a COSE_Key: EC2, ES256, P-256.
  private byte[] CoseKey() {
    var parameters = _key.ExportParameters(false);
    var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
    writer.WriteStartMap(5);
    writer.WriteInt32(1);
    writer.WriteInt32(2);
    writer.WriteInt32(3);
    writer.WriteInt32(-7);
    writer.WriteInt32(-1);
    writer.WriteInt32(1);
    writer.WriteInt32(-2);
    writer.WriteByteString(parameters.Q.X!);
    writer.WriteInt32(-3);
    writer.WriteByteString(parameters.Q.Y!);
    writer.WriteEndMap();
    return writer.Encode();
  }
}
