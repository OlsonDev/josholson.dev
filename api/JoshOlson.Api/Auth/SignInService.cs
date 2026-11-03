using System.Net;
using Fido2NetLib;
using NodaTime;

namespace JoshOlson.Api.Auth;

public sealed class SignInService {
  private readonly WebAuthnService _webAuthnService;
  private readonly SessionService _sessionService;
  private readonly PasskeyRepository _passkeyRepository;
  private readonly AuthUserRepository _authUserRepository;
  private readonly SingleUseStore<AssertionOptions> _challenges;
  private readonly IClock _clock;

  public SignInService(
    WebAuthnService webAuthnService,
    SessionService sessionService,
    PasskeyRepository passkeyRepository,
    AuthUserRepository authUserRepository,
    SingleUseStore<AssertionOptions> challenges,
    IClock clock
  ) {
    _webAuthnService = webAuthnService;
    _sessionService = sessionService;
    _passkeyRepository = passkeyRepository;
    _authUserRepository = authUserRepository;
    _challenges = challenges;
    _clock = clock;
  }

  // No username and no credential ids: the browser offers whichever passkey it holds, and an anonymous visitor
  // learns nothing about the account (ADR 0031, Red, Purple, or a phone).
  public CeremonyResponse<AssertionOptions> Start() {
    var options = _webAuthnService.CreateAssertionOptions([]);
    return new(_challenges.Add(options, ""), options);
  }

  // The new session's token, or null when anything about the assertion is wrong. The caller cannot tell which.
  public async Task<string?> FinishAsync(
    SignInRequest request,
    IPAddress? ip,
    string? userAgent,
    CancellationToken cancellationToken
  ) {
    var options = _challenges.Take(request.AttemptToken, "");
    if (options is null) return null;

    var passkey = await _passkeyRepository.GetByCredentialIdAsync(request.Response.RawId, cancellationToken);
    if (passkey is null) return null;
    var user = await _authUserRepository.GetAsync(passkey.UserId, cancellationToken);
    if (user is not { IsActive: true }) return null;

    var result = await _webAuthnService.VerifyAssertionAsync(
      request.Response,
      options,
      passkey,
      user,
      cancellationToken);
    if (result is null) return null;

    await _passkeyRepository.RecordUseAsync(
      passkey.PasskeyId,
      result.SignCount,
      result.IsBackedUp,
      _clock.GetCurrentInstant(),
      cancellationToken);
    return await _sessionService.StartAsync(user.UserId, ip, userAgent, cancellationToken);
  }
}
