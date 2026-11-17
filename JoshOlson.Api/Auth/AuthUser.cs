namespace JoshOlson.Api.Auth;

public sealed record AuthUser {
  public required int UserId { get; init; }
  public required string DisplayName { get; init; }

  // The opaque value WebAuthn stores beside each passkey (ADR 0031). Random, never derived from user data.
  public required byte[] UserHandle { get; init; }
  public required bool IsActive { get; init; }
}
