using System.ComponentModel.DataAnnotations;

namespace JoshOlson.Api.Auth;

// The `Auth` configuration section (ADR 0027, passkey auth, and ADR 0031, Red, Purple, or a phone). Production values
// come from the environment, for example `Auth__RelyingPartyId=josholson.dev`; development and tests use `localhost`.
public sealed class AuthOptions {
  public const string SectionName = "Auth";

  // The registrable domain, so a credential works from `www` and can be verified by `api`.
  [Required]
  public string RelyingPartyId { get; init; } = "localhost";

  [Required]
  public string RelyingPartyName { get; init; } = "josholson.dev";

  // The one origin that may call the API with credentials, and the only one WebAuthn accepts. An exact match.
  [Required]
  public string AllowedOrigin { get; init; } = "http://localhost:5173";

  // The cookie's `Domain`. Empty for `localhost`, where a domain cookie is not wanted.
  public string CookieDomain { get; init; } = "";

  public string CookieName { get; init; } = "__Secure-Session";

  public bool CookieSecure { get; init; } = true;

  public TimeSpan SessionIdleLifetime { get; init; } = TimeSpan.FromDays(7);

  public TimeSpan SessionAbsoluteLifetime { get; init; } = TimeSpan.FromDays(30);

  public TimeSpan EnrollmentTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

  // A pending registration, a pending change, and a challenge all expire after this long.
  public TimeSpan SingleUseLifetime { get; init; } = TimeSpan.FromMinutes(5);
}
