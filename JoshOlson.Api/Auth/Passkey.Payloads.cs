using System.ComponentModel.DataAnnotations;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Validation;
using NodaTime;

namespace JoshOlson.Api.Auth;

// A stored passkey, as the sign-in and the passkey page read it.
public sealed record Passkey {
  public required int PasskeyId { get; init; }
  public required int UserId { get; init; }
  public required byte[] CredentialId { get; init; }
  public required byte[] PublicKey { get; init; }
  public required long SignCount { get; init; }
  public required List<PasskeyTransport> Transports { get; init; }
  public required bool IsBackupEligible { get; init; }
  public required bool IsBackedUp { get; init; }
  public required string Nickname { get; init; }
  public required Instant CreatedUtc { get; init; }
  public required Instant? LastUsedUtc { get; init; }
}

// A verified registration, ready to store.
public sealed record NewPasskey {
  public required int UserId { get; init; }
  public required byte[] CredentialId { get; init; }
  public required byte[] PublicKey { get; init; }
  public required long SignCount { get; init; }
  public required List<PasskeyTransport> Transports { get; init; }
  public required bool IsBackupEligible { get; init; }
  public required bool IsBackedUp { get; init; }
  public required string Nickname { get; init; }
}

// What a verified registration yields, before a nickname and an owner make it a NewPasskey.
public sealed record VerifiedRegistration {
  public required byte[] CredentialId { get; init; }
  public required byte[] PublicKey { get; init; }
  public required long SignCount { get; init; }
  public required List<PasskeyTransport> Transports { get; init; }
  public required bool IsBackupEligible { get; init; }
  public required bool IsBackedUp { get; init; }

  public NewPasskey ToNewPasskey(int userId, string nickname)
    => new() {
      UserId = userId,
      CredentialId = CredentialId,
      PublicKey = PublicKey,
      SignCount = SignCount,
      Transports = Transports,
      IsBackupEligible = IsBackupEligible,
      IsBackedUp = IsBackedUp,
      Nickname = nickname,
    };
}

// State kept in memory between the steps of a ceremony (see SingleUseStore).
public sealed record EnrollmentChallenge(CredentialCreateOptions Options, byte[] TokenHash, int UserId);

public sealed record ConfirmChallenge(string ChangeToken, AssertionOptions Options);

// A change to the passkey set that takes effect only once a registered passkey confirms it (ADR 0031, pending passkey
// changes).
public abstract record PendingPasskeyChange;

public sealed record PendingAddition(VerifiedRegistration Registration, string Nickname) : PendingPasskeyChange;

public sealed record PendingRemoval(int PasskeyId) : PendingPasskeyChange;

// Every ceremony starts by returning the browser's options with the token of the challenge kept for them.
public sealed record CeremonyResponse<TOptions>(string AttemptToken, TOptions Options);

public sealed record PendingChangeResponse(string ChangeToken);

public sealed record PasskeyResponse {
  public required int PasskeyId { get; init; }
  public required string Nickname { get; init; }
  public required Instant CreatedUtc { get; init; }
  public required Instant? LastUsedUtc { get; init; }
  public required bool IsBackupEligible { get; init; }
  public required bool IsBackedUp { get; init; }
  public required List<PasskeyTransport> Transports { get; init; }
}

[ValidatableType]
public sealed record RegistrationRequest {
  public required string AttemptToken { get; init; }

  [Required, Trimmed, MaxLength(64)]
  public required string Nickname { get; init; }

  public required AuthenticatorAttestationRawResponse Response { get; init; }
}

public sealed record ConfirmationOptionsRequest {
  public required string ChangeToken { get; init; }
}

public sealed record ConfirmationRequest {
  public required string ChangeToken { get; init; }
  public required string AttemptToken { get; init; }
  public required AuthenticatorAssertionRawResponse Response { get; init; }
}

[ValidatableType]
public sealed record RenameRequest {
  [Required, Trimmed, MaxLength(64)]
  public required string Nickname { get; init; }
}
