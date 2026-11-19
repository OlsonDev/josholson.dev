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

/// <summary>Whether the page can register the first passkey from the browser, and what it has to ask for.</summary>
public sealed record EnrollmentStatusResponse {
  /// <summary>
  /// Whether setup is open: a setup secret is configured and no passkey is registered yet. Otherwise there is nothing
  /// to set up, and the page offers a sign-in.
  /// </summary>
  public required bool Available { get; init; }

  /// <summary>Whether setup also needs a name for the account, because there is none yet. Only meaningful when available.</summary>
  public required bool NeedsDisplayName { get; init; }
}

/// <summary>What a client sends to start setting up: the secret, and a name for the account if it has none yet.</summary>
[ValidatableType]
public sealed record EnrollmentSetupRequest {
  /// <summary>The setup secret the API was started with.</summary>
  public required string SetupSecret { get; init; }

  /// <summary>
  /// The name shown for the account. Needed only when there is no account yet, and ignored otherwise. Up to 64
  /// characters, with no leading or trailing whitespace.
  /// </summary>
  [Trimmed, MaxLength(64)]
  public string? DisplayName { get; init; }
}

/// <summary>A one-time enrollment token, to register the first passkey with.</summary>
public sealed record EnrollmentTokenResponse {
  /// <summary>The token to start enrolling with. It expires after 15 minutes.</summary>
  public required string Token { get; init; }
}

/// <summary>What a client sends to start enrolling a passkey with an enrollment token.</summary>
[ValidatableType]
public sealed record EnrollmentOptionsRequest {
  /// <summary>The one-time enrollment token from setup.</summary>
  public required string Token { get; init; }

  /// <summary>
  /// What to call the new passkey. The key stores it with the passkey, so a chooser can say which. Up to 64 characters,
  /// with no leading or trailing whitespace.
  /// </summary>
  [Required, Trimmed, MaxLength(64)]
  public required string Nickname { get; init; }
}

/// <summary>What the browser's WebAuthn API produced for the registration options, sent to finish enrolling.</summary>
public sealed record EnrollmentRequest {
  /// <summary>The enrollment token the options were started with.</summary>
  public required string Token { get; init; }

  /// <summary>The attempt token from the registration options this request answers.</summary>
  public required string AttemptToken { get; init; }

  /// <summary>The attestation the browser returned. The web relays it and never reads it.</summary>
  public required AuthenticatorAttestationRawResponse Response { get; init; }
}
