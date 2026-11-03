using System.ComponentModel.DataAnnotations;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Validation;

namespace JoshOlson.Api.Auth;

public sealed record SignInRequest {
  public required string AttemptToken { get; init; }
  public required AuthenticatorAssertionRawResponse Response { get; init; }
}

public sealed record CurrentUserResponse {
  public required int UserId { get; init; }
  public required string DisplayName { get; init; }
}

public sealed record EnrollmentOptionsRequest {
  public required string Token { get; init; }
}

[ValidatableType]
public sealed record EnrollmentRequest {
  public required string Token { get; init; }
  public required string AttemptToken { get; init; }

  [Required, Trimmed, MaxLength(64)]
  public required string Nickname { get; init; }

  public required AuthenticatorAttestationRawResponse Response { get; init; }
}
