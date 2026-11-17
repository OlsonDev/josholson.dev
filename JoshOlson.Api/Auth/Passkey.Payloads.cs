using System.ComponentModel.DataAnnotations;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Validation;
using NodaTime;

namespace JoshOlson.Api.Auth;

/// <summary>The browser's options for a ceremony, with the token of the challenge the API kept for them.</summary>
/// <typeparam name="TOptions">The WebAuthn options type for the ceremony.</typeparam>
/// <param name="AttemptToken">Sent back with the browser's response, so the API can find the challenge it kept.</param>
/// <param name="Options">What the browser passes to the WebAuthn API. The web relays it and never reads it.</param>
public sealed record CeremonyResponse<TOptions>(string AttemptToken, TOptions Options);

/// <summary>A change to the passkey set that waits for a registered passkey to confirm it.</summary>
/// <param name="ChangeToken">Names the pending change in the confirmation that follows.</param>
public sealed record PendingChangeResponse(string ChangeToken);

/// <summary>A registered passkey as the passkey page lists it.</summary>
public sealed record PasskeyResponse {
  /// <summary>The passkey's ID, which its URL names.</summary>
  public required int PasskeyId { get; init; }

  /// <summary>What its owner called it, such as the key's color.</summary>
  public required string Nickname { get; init; }

  /// <summary>When the passkey was registered.</summary>
  public required Instant CreatedUtc { get; init; }

  /// <summary>When it last signed in, or null if it never has.</summary>
  public required Instant? LastUsedUtc { get; init; }

  /// <summary>Whether the credential can be backed up or synced (WebAuthn's backup eligibility flag).</summary>
  public required bool IsBackupEligible { get; init; }

  /// <summary>Whether the credential currently is backed up (WebAuthn's backup state flag).</summary>
  public required bool IsBackedUp { get; init; }

  /// <summary>How the authenticator can be reached, as it reported at registration.</summary>
  public required List<PasskeyTransport> Transports { get; init; }
}

/// <summary>What the browser's WebAuthn API produced for the registration options, sent to add a passkey.</summary>
[ValidatableType]
public sealed record RegistrationRequest {
  /// <summary>The attempt token from the registration options this request answers.</summary>
  public required string AttemptToken { get; init; }

  /// <summary>What to call the new passkey. Up to 64 characters, with no leading or trailing whitespace.</summary>
  [Required, Trimmed, MaxLength(64)]
  public required string Nickname { get; init; }

  /// <summary>The attestation the browser returned. The web relays it and never reads it.</summary>
  public required AuthenticatorAttestationRawResponse Response { get; init; }
}

/// <summary>What a client sends to ask for the options that confirm a pending change.</summary>
public sealed record ConfirmationOptionsRequest {
  /// <summary>The pending change to confirm, from the response that started it.</summary>
  public required string ChangeToken { get; init; }
}

/// <summary>What the browser's WebAuthn API produced for the confirmation options, sent to apply the change.</summary>
public sealed record ConfirmationRequest {
  /// <summary>The pending change to confirm.</summary>
  public required string ChangeToken { get; init; }

  /// <summary>The attempt token from the confirmation options this request answers.</summary>
  public required string AttemptToken { get; init; }

  /// <summary>The signed assertion the browser returned. The web relays it and never reads it.</summary>
  public required AuthenticatorAssertionRawResponse Response { get; init; }
}

/// <summary>What a client sends to rename a passkey.</summary>
[ValidatableType]
public sealed record RenameRequest {
  /// <summary>The new name. Up to 64 characters, with no leading or trailing whitespace.</summary>
  [Required, Trimmed, MaxLength(64)]
  public required string Nickname { get; init; }
}
