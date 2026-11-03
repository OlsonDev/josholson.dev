using System.Net;
using System.Security.Cryptography;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgsql;

namespace JoshOlson.Api.Auth;

// The first passkey, and recovery when every passkey is lost (ADR 0027, bootstrap and recovery): a token minted on the
// server by the CLI starts a registration that needs no session.
public sealed class EnrollmentService {
  private readonly NpgsqlConnection _connection;
  private readonly EnrollmentTokenRepository _enrollmentTokenRepository;
  private readonly AuthUserRepository _authUserRepository;
  private readonly PasskeyRepository _passkeyRepository;
  private readonly WebAuthnService _webAuthnService;
  private readonly SessionService _sessionService;
  private readonly PayloadValidator _validator;
  private readonly SingleUseStore<EnrollmentChallenge> _challenges;
  private readonly IClock _clock;
  private readonly AuthOptions _options;

  public EnrollmentService(
    NpgsqlConnection connection,
    EnrollmentTokenRepository enrollmentTokenRepository,
    AuthUserRepository authUserRepository,
    PasskeyRepository passkeyRepository,
    WebAuthnService webAuthnService,
    SessionService sessionService,
    PayloadValidator validator,
    SingleUseStore<EnrollmentChallenge> challenges,
    IClock clock,
    IOptions<AuthOptions> options
  ) {
    _connection = connection;
    _enrollmentTokenRepository = enrollmentTokenRepository;
    _authUserRepository = authUserRepository;
    _passkeyRepository = passkeyRepository;
    _webAuthnService = webAuthnService;
    _sessionService = sessionService;
    _validator = validator;
    _challenges = challenges;
    _clock = clock;
    _options = options.Value;
  }

  // The CLI's work: mint a token for the account's owner, creating the account on first use. `displayName` is only
  // used when there is no account yet. Returns the token, which is never stored.
  public async Task<string> CreateTokenAsync(string? displayName, CancellationToken cancellationToken) {
    var owner = await _authUserRepository.GetFirstAsync(cancellationToken);
    int userId;
    if (owner is not null) {
      userId = owner.UserId;
    } else {
      if (string.IsNullOrWhiteSpace(displayName))
        throw new ArgumentException("There is no account yet, so a display name is required.", nameof(displayName));
      var handle = OpaqueToken.NewUserHandle();
      userId = await _authUserRepository.CreateAsync(displayName.Trim(), handle, cancellationToken);
    }

    var token = OpaqueToken.Create();
    var expiresUtc = _clock.GetCurrentInstant() + Duration.FromTimeSpan(_options.EnrollmentTokenLifetime);
    await _enrollmentTokenRepository.CreateAsync(userId, OpaqueToken.Hash(token), expiresUtc, cancellationToken);
    return token;
  }

  // Null when the token is unknown, used, or expired.
  public async Task<CeremonyResponse<CredentialCreateOptions>?> StartAsync(
    string token,
    CancellationToken cancellationToken
  ) {
    var tokenHash = OpaqueToken.Hash(token);
    var nowUtc = _clock.GetCurrentInstant();
    var userId = await _enrollmentTokenRepository.FindUserIdAsync(tokenHash, nowUtc, cancellationToken);
    if (userId is null) return null;
    var user = await _authUserRepository.GetAsync(userId.Value, cancellationToken);
    if (user is not { IsActive: true }) return null;

    var existing = await _passkeyRepository.GetAllAsync(user.UserId, cancellationToken);
    var options = _webAuthnService.CreateRegistrationOptions(user, existing);
    return new(_challenges.Add(new(options, tokenHash, user.UserId), ""), options);
  }

  public async Task<ValidationErrors> ValidateFinishAsync(
    EnrollmentRequest request,
    CancellationToken cancellationToken
  ) {
    var errors = await _validator.ValidateAsync(request, cancellationToken);
    if (errors.Has("nickname")) return errors;
    var userId = await _enrollmentTokenRepository.FindUserIdAsync(
      OpaqueToken.Hash(request.Token),
      _clock.GetCurrentInstant(),
      cancellationToken);
    if (userId is null) return errors;
    var existing = await _passkeyRepository.GetAllAsync(userId.Value, cancellationToken);
    if (existing.Any(passkey => string.Equals(passkey.Nickname, request.Nickname, StringComparison.Ordinal)))
      errors.Add("nickname", "Nickname is already in use.");
    return errors;
  }

  // The new session's token, or null when the token, the challenge, or the registration is wrong. The token is used
  // up in the same transaction that stores the passkey, so a failure leaves it usable.
  public async Task<string?> FinishAsync(
    EnrollmentRequest request,
    IPAddress? ip,
    string? userAgent,
    CancellationToken cancellationToken
  ) {
    var challenge = _challenges.Take(request.AttemptToken, "");
    var tokenHash = OpaqueToken.Hash(request.Token);
    if (challenge is null || !CryptographicOperations.FixedTimeEquals(challenge.TokenHash, tokenHash)) return null;

    var registration = await _webAuthnService.VerifyRegistrationAsync(
      request.Response,
      challenge.Options,
      cancellationToken);
    if (registration is null) return null;

    await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
    var claimed = await _enrollmentTokenRepository.UseAsync(tokenHash, _clock.GetCurrentInstant(), cancellationToken);
    if (!claimed) return null;
    var passkey = registration.ToNewPasskey(challenge.UserId, request.Nickname);
    await _passkeyRepository.CreateAsync(passkey, cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return await _sessionService.StartAsync(challenge.UserId, ip, userAgent, cancellationToken);
  }
}
