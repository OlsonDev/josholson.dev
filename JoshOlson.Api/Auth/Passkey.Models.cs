using Fido2NetLib;
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
public sealed record EnrollmentChallenge(
  CredentialCreateOptions Options,
  byte[] TokenHash,
  int UserId,
  string Nickname
);

// The registration options of a passkey being added, with the nickname they were made for (the key stores it too).
public sealed record PendingRegistration(CredentialCreateOptions Options, string Nickname);

public sealed record ConfirmChallenge(string ChangeToken, AssertionOptions Options);

// A change to the passkey set that takes effect only once a registered passkey confirms it (ADR 0031, pending passkey
// changes). It is `closed`, so a switch over it that misses a case does not compile.
public closed record PendingPasskeyChange;

public sealed record PendingAddition(VerifiedRegistration Registration, string Nickname) : PendingPasskeyChange;

public sealed record PendingRemoval(int PasskeyId) : PendingPasskeyChange;

// What asking to remove a passkey comes to: the pending change to confirm, or one of the two reasons there is none.
// The controller gives each case a status code, and a case added here does not compile there until it has one.
public readonly union RemovalResult(PendingChangeResponse, PasskeyNotFound, LastPasskey);

// The user has no passkey with that ID.
public sealed record PasskeyNotFound;

// It is the user's only passkey, and removing it would lock them out.
public sealed record LastPasskey;
