using NodaTime;

namespace JoshOlson.Api.Auth;

// A stored passkey, as the sign-in and the passkey page read it.
public sealed record Passkey {
  public required int PasskeyId { get; init; }
  public required int UserId { get; init; }
  public required byte[] CredentialId { get; init; }
  public required byte[] PublicKey { get; init; }
  public required long SignCount { get; init; }
  public required IReadOnlyList<PasskeyTransport> Transports { get; init; }
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
  public required IReadOnlyList<PasskeyTransport> Transports { get; init; }
  public required bool IsBackupEligible { get; init; }
  public required bool IsBackedUp { get; init; }
  public required string Nickname { get; init; }
}
