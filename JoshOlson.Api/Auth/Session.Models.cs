using System.Net;
using NodaTime;

namespace JoshOlson.Api.Auth;

// A session found by its token hash, with the user it signs in as.
public sealed record SessionInfo {
  public required int SessionId { get; init; }
  public required int UserId { get; init; }
  public required Instant CreatedUtc { get; init; }
  public required Instant ExpiresUtc { get; init; }
}

public sealed record NewSession {
  public required int UserId { get; init; }
  public required byte[] TokenHash { get; init; }
  public required Instant ExpiresUtc { get; init; }
  public required IPAddress? Ip { get; init; }
  public required int? UserAgentId { get; init; }
}

// The signed-in user and the session they signed in with. Kept on the request by `SessionFilter`.
public sealed record SessionPrincipal(AuthUser User, int SessionId);
