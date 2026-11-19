using System.Net;
using System.Security.Cryptography;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgsql;

namespace JoshOlson.Api.Auth;

// The first passkey, from the browser: the setup secret, while no passkey exists, mints a one-time token, and the token
// starts a registration that needs no session. Every later passkey is added from the passkey page, signed in, and
// confirmed by one that exists.
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

  // Setup is open when a setup secret is configured and no passkey is registered yet. It closes by itself with the first
  // passkey, so the secret does nothing afterwards. Losing every passkey does not reopen it: that is a person with
  // access to the database deleting the rows (`DELETE FROM auth.passkey`).
  private async Task<bool> IsSetupOpenAsync(CancellationToken cancellationToken)
    => _options.SetupSecret.Length > 0 && !await _passkeyRepository.AnyAsync(cancellationToken);

  public async Task<EnrollmentStatusResponse> GetStatusAsync(CancellationToken cancellationToken) {
    if (!await IsSetupOpenAsync(cancellationToken)) {
      return new() {
        Available = false,
        NeedsDisplayName = false,
      };
    }
    var owner = await _authUserRepository.GetFirstAsync(cancellationToken);
    return new() {
      Available = true,
      NeedsDisplayName = owner is null,
    };
  }

  // True when setup is open and the secret is the configured one.
  public async Task<bool> AllowsSetupAsync(string setupSecret, CancellationToken cancellationToken)
    => SetupSecret.Matches(_options.SetupSecret, setupSecret) && await IsSetupOpenAsync(cancellationToken);

  public async Task<ValidationErrors> ValidateSetupAsync(
    EnrollmentSetupRequest request,
    CancellationToken cancellationToken
  ) {
    var errors = await _validator.ValidateAsync(request, cancellationToken);
    var needsName = string.IsNullOrWhiteSpace(request.DisplayName)
      && await _authUserRepository.GetFirstAsync(cancellationToken) is null;
    if (!errors.Has("displayName") && needsName) errors.Add("displayName", "Name is required.");
    return errors;
  }

  // Mints a token for the account's owner, creating the account on first use. `displayName` is only used when there is
  // no account yet. Returns the token, which is never stored.
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

  // The nickname is known before the key makes anything, so it is checked now and goes into the options, which the key
  // keeps with the passkey. A token that is unknown is not reported here: starting answers that.
  public async Task<ValidationErrors> ValidateStartAsync(
    EnrollmentOptionsRequest request,
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

  // Null when the token is unknown, used, or expired.
  public async Task<CeremonyResponse<CredentialCreateOptions>?> StartAsync(
    EnrollmentOptionsRequest request,
    CancellationToken cancellationToken
  ) {
    var tokenHash = OpaqueToken.Hash(request.Token);
    var nowUtc = _clock.GetCurrentInstant();
    var userId = await _enrollmentTokenRepository.FindUserIdAsync(tokenHash, nowUtc, cancellationToken);
    if (userId is null) return null;
    var user = await _authUserRepository.GetAsync(userId.Value, cancellationToken);
    if (user is not { IsActive: true }) return null;

    var existing = await _passkeyRepository.GetAllAsync(user.UserId, cancellationToken);
    var options = _webAuthnService.CreateRegistrationOptions(user, existing, request.Nickname);
    return new(_challenges.Add(new(options, tokenHash, user.UserId, request.Nickname), ""), options);
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
    var passkey = registration.ToNewPasskey(challenge.UserId, challenge.Nickname);
    await _passkeyRepository.CreateAsync(passkey, cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return await _sessionService.StartAsync(challenge.UserId, ip, userAgent, cancellationToken);
  }
}
