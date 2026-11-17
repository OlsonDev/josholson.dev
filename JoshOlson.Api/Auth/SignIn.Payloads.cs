using System.ComponentModel.DataAnnotations;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Validation;

namespace JoshOlson.Api.Auth;

/// <summary>What the browser's WebAuthn API produced for the sign-in options, sent to sign in.</summary>
public sealed record SignInRequest {
  /// <summary>The attempt token from the sign-in options this request answers.</summary>
  public required string AttemptToken { get; init; }

  /// <summary>The signed assertion the browser returned. The web relays it and never reads it.</summary>
  public required AuthenticatorAssertionRawResponse Response { get; init; }
}

/// <summary>The user a session signs in as.</summary>
public sealed record CurrentUserResponse {
  /// <summary>The user's ID.</summary>
  public required int UserId { get; init; }

  /// <summary>The name shown for the user.</summary>
  public required string DisplayName { get; init; }
}

/// <summary>What a client sends to start enrolling a passkey: the first, or a replacement when all are lost.</summary>
public sealed record EnrollmentOptionsRequest {
  /// <summary>The one-time enrollment token minted by the enroll command. It expires after 15 minutes.</summary>
  public required string Token { get; init; }
}

/// <summary>What the browser's WebAuthn API produced for the registration options, sent to finish enrolling.</summary>
[ValidatableType]
public sealed record EnrollmentRequest {
  /// <summary>The enrollment token the options were started with.</summary>
  public required string Token { get; init; }

  /// <summary>The attempt token from the registration options this request answers.</summary>
  public required string AttemptToken { get; init; }

  /// <summary>What to call the new passkey. Up to 64 characters, with no leading or trailing whitespace.</summary>
  [Required, Trimmed, MaxLength(64)]
  public required string Nickname { get; init; }

  /// <summary>The attestation the browser returned. The web relays it and never reads it.</summary>
  public required AuthenticatorAttestationRawResponse Response { get; init; }
}
