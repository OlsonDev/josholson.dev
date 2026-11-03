#!/usr/bin/env dotnet
#:package Fido2
#:package Flurl.Http
#:property PublishAot=false
// Plays the browser against a running API with a real security key (Red or Purple), through libfido2's fido2-cred and
// fido2-assert, so the passkey endpoints can be tried without a front end (ADR 0027, passkey auth, and ADR 0031,
// Red, Purple, or a phone). It asks for the key's PIN and a touch for each step.
//
//   dotnet run scripts/passkey-cli.cs -- enroll <token>
//       Registers the key with an enrollment token (`dotnet run --project JoshOlson.Api -- enroll "Name"`), then
//       signs in with it and prints the session's user and passkeys. Prints the ids that `sign-in` needs.
//   dotnet run scripts/passkey-cli.cs -- sign-in --credential-id <base64url> --user-handle <base64url>
//       Signs in with an already registered key.
//
// Options: --api (default http://localhost:5000), --origin (default http://localhost:5173, the API's AllowedOrigin),
// --rp-id (default localhost, the API's RelyingPartyId), --device (a /dev/hidraw path; needed when two keys are
// plugged in), --nickname (default "key", for enroll).
//
// What the script does that a browser also does: it sends the allowed `Origin`, builds the client data, and zeroes the
// AAGUID in the authenticator data, because attestation is `none` (ADR 0031, no AAGUID column). The credential is bound
// to --rp-id, so a key registered for `localhost` is a development credential and must be registered again for
// production.
using System.Buffers.Text;
using System.Diagnostics;
using System.Formats.Cbor;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Flurl.Http;

var settings = Settings.Parse(args);
if (settings is null) {
  Console.Error.WriteLine("Usage: dotnet run scripts/passkey-cli.cs -- <command> [options]");
  Console.Error.WriteLine("  enroll <token>");
  Console.Error.WriteLine("  sign-in --credential-id <id> --user-handle <id>");
  Console.Error.WriteLine("Options: --api, --origin, --rp-id, --device, --nickname (see the top of the script).");
  return 2;
}

try {
  var key = new SecurityKey(await SecurityKey.FindDeviceAsync(settings.Device), settings.RpId, settings.Origin);
  var api = new ApiClient(settings.Api, settings.Origin);
  return settings.Command switch {
    "enroll" => await Flows.EnrollAsync(settings, key, api),
    _ => await Flows.SignInAsync(settings, key, api),
  };
} catch (Exception exception) when (exception is InvalidOperationException or FlurlHttpException or IOException) {
  Console.Error.WriteLine(exception.Message);
  return 1;
}

internal sealed record Settings(
  string Command,
  string? Token,
  string Api,
  string Origin,
  string RpId,
  string? Device,
  string Nickname,
  string? CredentialId,
  string? UserHandle
) {
  public static Settings? Parse(string[] args) {
    var command = args.FirstOrDefault();
    if (command is not ("enroll" or "sign-in")) return null;

    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    var positional = new List<string>();
    for (var index = 1; index < args.Length; index++) {
      if (!args[index].StartsWith("--", StringComparison.Ordinal)) {
        positional.Add(args[index]);
      } else if (index + 1 < args.Length) {
        options[args[index]] = args[++index];
      } else {
        return null;
      }
    }

    var token = positional.FirstOrDefault();
    if (command is "enroll" && token is null) return null;
    var hasIds = options.ContainsKey("--credential-id") && options.ContainsKey("--user-handle");
    if (command is not "enroll" && !hasIds) return null;
    return new(
      command,
      token,
      options.GetValueOrDefault("--api", "http://localhost:5000"),
      options.GetValueOrDefault("--origin", "http://localhost:5173"),
      options.GetValueOrDefault("--rp-id", "localhost"),
      options.GetValueOrDefault("--device"),
      options.GetValueOrDefault("--nickname", "key"),
      options.GetValueOrDefault("--credential-id"),
      options.GetValueOrDefault("--user-handle"));
  }
}

internal static class Flows {
  public static async Task<int> EnrollAsync(Settings settings, SecurityKey key, ApiClient api) {
    var started = await api.PostAsync("/auth/enrollment/options", new { token = settings.Token }, null);
    started.EnsureSuccess("Starting the enrollment");
    var ceremony = started.Read<Ceremony<CredentialCreateOptions>>();

    Console.WriteLine("Registering the key: enter its PIN, then touch it.");
    var attestation = await key.MakeCredentialAsync(ceremony.Options);
    var enrollment = new {
      token = settings.Token,
      attemptToken = ceremony.AttemptToken,
      nickname = settings.Nickname,
      response = attestation,
    };
    var enrolled = await api.PostAsync("/auth/enrollment", enrollment, null);
    enrolled.EnsureSuccess("Enrolling");
    Console.WriteLine("Enrolled. The API signed this client in:");
    await PrintSessionAsync(api, enrolled.Cookie);

    var credentialId = Base64Url.EncodeToString(attestation.RawId);
    var userHandle = Base64Url.EncodeToString(ceremony.Options.User.Id);
    Console.WriteLine($"Credential id: {credentialId}");
    Console.WriteLine($"User handle:   {userHandle}");

    Console.WriteLine("Now signing in with the same key: enter its PIN, then touch it again.");
    await SignInWithAsync(key, api, attestation.RawId, ceremony.Options.User.Id);
    return 0;
  }

  public static async Task<int> SignInAsync(Settings settings, SecurityKey key, ApiClient api) {
    var credentialId = Base64Url.DecodeFromChars(settings.CredentialId);
    var userHandle = Base64Url.DecodeFromChars(settings.UserHandle);
    Console.WriteLine("Signing in: enter the key's PIN, then touch it.");
    await SignInWithAsync(key, api, credentialId, userHandle);
    return 0;
  }

  // Returns the session cookie.
  private static async Task<string> SignInWithAsync(
    SecurityKey key,
    ApiClient api,
    byte[] credentialId,
    byte[] userHandle
  ) {
    var started = await api.PostAsync("/auth/sign-in/options", new { }, null);
    started.EnsureSuccess("Starting the sign-in");
    var ceremony = started.Read<Ceremony<AssertionOptions>>();

    var assertion = await key.GetAssertionAsync(ceremony.Options, credentialId, userHandle);
    var request = new {
      attemptToken = ceremony.AttemptToken,
      response = assertion,
    };
    var signedIn = await api.PostAsync("/auth/sign-in", request, null);
    signedIn.EnsureSuccess("Signing in");
    Console.WriteLine("Signed in:");
    await PrintSessionAsync(api, signedIn.Cookie);
    return signedIn.Cookie ?? throw new InvalidOperationException("The API set no session cookie.");
  }

  private static async Task PrintSessionAsync(ApiClient api, string? cookie) {
    var me = await api.GetAsync("/auth/me", cookie);
    me.EnsureSuccess("Reading the session");
    Console.WriteLine($"  /auth/me       {me.Body}");
    var passkeys = await api.GetAsync("/auth/passkeys", cookie);
    passkeys.EnsureSuccess("Listing the passkeys");
    Console.WriteLine($"  /auth/passkeys {passkeys.Body}");
  }
}

internal sealed record Ceremony<TOptions>(string AttemptToken, TOptions Options);

internal sealed record ApiResponse(int Status, string Body, string? Cookie) {
  private static readonly JsonSerializerOptions _Json = new(JsonSerializerDefaults.Web);

  public void EnsureSuccess(string what) {
    if (Status is < 200 or > 299) {
      throw new InvalidOperationException(
        string.Create(CultureInfo.InvariantCulture, $"{what} failed: HTTP {Status} {Body}"));
    }
  }

  public T Read<T>()
    => JsonSerializer.Deserialize<T>(Body, _Json) ?? throw new InvalidOperationException("The API returned no body.");
}

// HTTP with Flurl. The session cookie is carried by hand: the API marks it `Secure`, which a cookie jar would refuse to
// send over plain http://localhost.
internal sealed class ApiClient {
  private static readonly JsonSerializerOptions _Json = new(JsonSerializerDefaults.Web);

  private readonly string _api;
  private readonly string _origin;

  public ApiClient(string api, string origin) {
    _api = api.TrimEnd('/');
    _origin = origin;
  }

  public async Task<ApiResponse> PostAsync(string path, object body, string? cookie) {
    var json = JsonSerializer.Serialize(body, _Json);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");
    var response = await Request(path, cookie).PostAsync(content, cancellationToken: CancellationToken.None);
    return await ToResponseAsync(response);
  }

  public async Task<ApiResponse> GetAsync(string path, string? cookie)
    => await ToResponseAsync(await Request(path, cookie).GetAsync(cancellationToken: CancellationToken.None));

  private IFlurlRequest Request(string path, string? cookie) {
    var request = $"{_api}{path}".WithHeader("Origin", _origin).AllowAnyHttpStatus();
    return cookie is null ? request : request.WithHeader("Cookie", cookie);
  }

  private static async Task<ApiResponse> ToResponseAsync(IFlurlResponse response) {
    var setCookie = response.ResponseMessage.Headers.TryGetValues("Set-Cookie", out var values)
      ? values.First().Split(';')[0]
      : null;
    return new(response.StatusCode, await response.GetStringAsync(), setCookie);
  }
}

// A FIDO2 security key, driven through libfido2's command line tools.
internal sealed class SecurityKey {
  private const byte _AttestedCredentialDataFlag = 0x40;
  private const int _AaguidOffset = 37;
  private const int _AaguidLength = 16;

  private readonly string _device;
  private readonly string _rpId;
  private readonly string _origin;

  public SecurityKey(string device, string rpId, string origin) {
    _device = device;
    _rpId = rpId;
    _origin = origin;
  }

  // The only plugged-in key, or the one named with --device.
  public static async Task<string> FindDeviceAsync(string? device) {
    if (device is not null) return device;
    var devices = (await RunAsync("fido2-token", ["-L"], [])).Select(line => line.Split(':')[0]).ToList();
    return devices switch {
      [var only] => only,
      [] => throw new InvalidOperationException("No FIDO2 key found. Plug one in."),
      _ => throw new InvalidOperationException(
        $"Several keys found ({string.Join(", ", devices)}). Pick one with --device."),
    };
  }

  // A discoverable credential with user verification, as the API asks for (ADR 0031, Red, Purple, or a phone).
  public async Task<AuthenticatorAttestationRawResponse> MakeCredentialAsync(CredentialCreateOptions options) {
    var clientData = ClientData("webauthn.create", options.Challenge);
    var input = new[] {
      Convert.ToBase64String(SHA256.HashData(clientData)),
      _rpId,
      options.User.Name,
      Convert.ToBase64String(options.User.Id),
    };
    var output = await RunAsync("fido2-cred", ["-M", "-r", "-v", _device, "es256"], input);
    var authenticatorData = DecodeAuthenticatorData(output[3]);
    var credentialId = Convert.FromBase64String(output[4]);

    // Attestation is `none`: a browser drops the statement and clears the AAGUID, so the API sees all zeros.
    if ((authenticatorData[32] & _AttestedCredentialDataFlag) is not 0) {
      var aaguid = new Guid(authenticatorData.AsSpan(_AaguidOffset, _AaguidLength), bigEndian: true);
      Console.WriteLine($"The key reported AAGUID {aaguid}; sending zeros, as a browser does with attestation 'none'.");
      authenticatorData.AsSpan(_AaguidOffset, _AaguidLength).Clear();
    }
    return new() {
      Id = Base64Url.EncodeToString(credentialId),
      RawId = credentialId,
      Type = PublicKeyCredentialType.PublicKey,
      ClientExtensionResults = new(),
      Response = new() {
        AttestationObject = NoneAttestationObject(authenticatorData),
        ClientDataJson = clientData,
        Transports = [AuthenticatorTransport.Usb],
      },
    };
  }

  public async Task<AuthenticatorAssertionRawResponse> GetAssertionAsync(
    AssertionOptions options,
    byte[] credentialId,
    byte[] userHandle
  ) {
    var clientData = ClientData("webauthn.get", options.Challenge);
    var input = new[] {
      Convert.ToBase64String(SHA256.HashData(clientData)),
      _rpId,
      Convert.ToBase64String(credentialId),
    };
    var output = await RunAsync("fido2-assert", ["-G", "-v", _device], input);
    return new() {
      Id = Base64Url.EncodeToString(credentialId),
      RawId = credentialId,
      Type = PublicKeyCredentialType.PublicKey,
      ClientExtensionResults = new(),
      Response = new() {
        AuthenticatorData = DecodeAuthenticatorData(output[2]),
        ClientDataJson = clientData,
        Signature = Convert.FromBase64String(output[3]),
        // The key does not return the handle for a credential named in the request, so it is passed along.
        UserHandle = userHandle,
      },
    };
  }

  private byte[] ClientData(string type, byte[] challenge)
    => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> {
      ["type"] = type,
      ["challenge"] = Base64Url.EncodeToString(challenge),
      ["origin"] = _origin,
      ["crossOrigin"] = false,
    });

  // libfido2 prints the authenticator data as a CBOR byte string.
  private static byte[] DecodeAuthenticatorData(string base64)
    => new CborReader(Convert.FromBase64String(base64)).ReadByteString();

  private static byte[] NoneAttestationObject(byte[] authenticatorData) {
    var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
    writer.WriteStartMap(3);
    writer.WriteTextString("fmt");
    writer.WriteTextString("none");
    writer.WriteTextString("attStmt");
    writer.WriteStartMap(0);
    writer.WriteEndMap();
    writer.WriteTextString("authData");
    writer.WriteByteString(authenticatorData);
    writer.WriteEndMap();
    return writer.Encode();
  }

  // Runs a libfido2 tool with the parameters on stdin. Its stderr stays on the terminal, where it asks for the PIN.
  private static async Task<string[]> RunAsync(string program, string[] arguments, string[] input) {
    var info = new ProcessStartInfo(program) {
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
    };
    foreach (var argument in arguments) {
      info.ArgumentList.Add(argument);
    }
    using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {program}.");
    await process.StandardInput.WriteAsync(string.Join('\n', input).AsMemory(), CancellationToken.None);
    process.StandardInput.Close();
    var output = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
    await process.WaitForExitAsync(CancellationToken.None);
    if (process.ExitCode is not 0) {
      throw new InvalidOperationException(
        string.Create(CultureInfo.InvariantCulture, $"{program} exited with {process.ExitCode}."));
    }
    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
  }
}
