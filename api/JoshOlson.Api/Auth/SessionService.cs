using System.Net;
using JoshOlson.Api.Common.Extensions;
using JoshOlson.Api.Http;
using Microsoft.Extensions.Options;
using NodaTime;

namespace JoshOlson.Api.Auth;

public sealed class SessionService {
  // A session's expiration is only rewritten when it would move by at least this much, so most requests do not write.
  // The stored value can therefore lag by up to this much, which shortens an idle session by at most that.
  private static readonly Duration _ExtensionThreshold = Duration.FromHours(1);

  private readonly SessionRepository _sessionRepository;
  private readonly AuthUserRepository _authUserRepository;
  private readonly UserAgentRepository _userAgentRepository;
  private readonly IClock _clock;
  private readonly AuthOptions _options;

  public SessionService(
    SessionRepository sessionRepository,
    AuthUserRepository authUserRepository,
    UserAgentRepository userAgentRepository,
    IClock clock,
    IOptions<AuthOptions> options
  ) {
    _sessionRepository = sessionRepository;
    _authUserRepository = authUserRepository;
    _userAgentRepository = userAgentRepository;
    _clock = clock;
    _options = options.Value;
  }

  public async Task<string> StartAsync(
    int userId,
    IPAddress? ip,
    string? userAgent,
    CancellationToken cancellationToken
  ) {
    var token = OpaqueToken.Create();
    var expiresUtc = _clock.GetCurrentInstant() + Duration.FromTimeSpan(_options.SessionIdleLifetime);
    var session = new NewSession {
      UserId = userId,
      TokenHash = OpaqueToken.Hash(token),
      ExpiresUtc = expiresUtc,
      Ip = ip,
      UserAgentId = await GetUserAgentIdAsync(userAgent, cancellationToken),
    };
    await _sessionRepository.CreateAsync(session, cancellationToken);
    return token;
  }

  // The user a session token signs in as, or null when the token is unknown, expired, or its user is inactive.
  public async Task<SessionPrincipal?> AuthenticateAsync(string token, CancellationToken cancellationToken) {
    var now = _clock.GetCurrentInstant();
    var tokenHash = OpaqueToken.Hash(token);
    var session = await _sessionRepository.GetActiveAsync(tokenHash, now, cancellationToken);
    if (session is null) return null;
    if (session.CreatedUtc + Duration.FromTimeSpan(_options.SessionAbsoluteLifetime) <= now) {
      await _sessionRepository.DeleteAsync(tokenHash, cancellationToken);
      return null;
    }

    var user = await _authUserRepository.GetAsync(session.UserId, cancellationToken);
    if (user is not { IsActive: true }) return null;

    // The idle expiration slides forward, but never past the absolute limit.
    var extendedExpiresUtc = Instant.Min(
      now + Duration.FromTimeSpan(_options.SessionIdleLifetime),
      session.CreatedUtc + Duration.FromTimeSpan(_options.SessionAbsoluteLifetime));
    if (extendedExpiresUtc - session.ExpiresUtc >= _ExtensionThreshold) {
      await _sessionRepository.ExtendExpirationAsync(session.SessionId, now, extendedExpiresUtc, cancellationToken);
    }
    return new(user, session.SessionId);
  }

  public Task<bool> EndAsync(string token, CancellationToken cancellationToken)
    => _sessionRepository.DeleteAsync(OpaqueToken.Hash(token), cancellationToken);

  // A blank header is no user agent, and an over-long one is cut to what the table holds.
  private async Task<int?> GetUserAgentIdAsync(string? userAgent, CancellationToken cancellationToken) {
    var cleaned = userAgent.TrimToNull()?.Truncate(UserAgentRepository.MaxLength);
    return cleaned is null ? null : await _userAgentRepository.GetOrCreateIdAsync(cleaned, cancellationToken);
  }
}
