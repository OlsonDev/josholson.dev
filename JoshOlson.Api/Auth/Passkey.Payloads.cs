using System.ComponentModel.DataAnnotations;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Validation;
using NodaTime;

namespace JoshOlson.Api.Auth;

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
