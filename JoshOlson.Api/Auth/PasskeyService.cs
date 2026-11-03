using System.Globalization;
using Fido2NetLib;
using JoshOlson.Api.Common.Validation;
using NodaTime;
using Npgsql;

namespace JoshOlson.Api.Auth;

// The passkey page (ADR 0031, Red, Purple, or a phone). Every change to the set is two ceremonies: the change itself,
// which is held in memory, and a confirmation with a passkey that is already registered. The new credential can never
// confirm itself.
public sealed class PasskeyService {
  private readonly NpgsqlConnection _connection;
  private readonly PasskeyRepository _passkeyRepository;
  private readonly WebAuthnService _webAuthnService;
  private readonly PayloadValidator _validator;
  private readonly SingleUseStore<CredentialCreateOptions> _registrationChallenges;
  private readonly SingleUseStore<PendingPasskeyChange> _changes;
  private readonly SingleUseStore<ConfirmChallenge> _confirmChallenges;
  private readonly IClock _clock;

  public PasskeyService(
    NpgsqlConnection connection,
    PasskeyRepository passkeyRepository,
    WebAuthnService webAuthnService,
    PayloadValidator validator,
    SingleUseStore<CredentialCreateOptions> registrationChallenges,
    SingleUseStore<PendingPasskeyChange> changes,
    SingleUseStore<ConfirmChallenge> confirmChallenges,
    IClock clock
  ) {
    _connection = connection;
    _passkeyRepository = passkeyRepository;
    _webAuthnService = webAuthnService;
    _validator = validator;
    _registrationChallenges = registrationChallenges;
    _changes = changes;
    _confirmChallenges = confirmChallenges;
    _clock = clock;
  }

  public async Task<List<PasskeyResponse>> GetAllAsync(int userId, CancellationToken cancellationToken) {
    var passkeys = await _passkeyRepository.GetAllAsync(userId, cancellationToken);
    List<PasskeyResponse> responses = new(passkeys.Count);
    foreach (var passkey in passkeys) {
      responses.Add(new() {
        PasskeyId = passkey.PasskeyId,
        Nickname = passkey.Nickname,
        CreatedUtc = passkey.CreatedUtc,
        LastUsedUtc = passkey.LastUsedUtc,
        IsBackupEligible = passkey.IsBackupEligible,
        IsBackedUp = passkey.IsBackedUp,
        Transports = passkey.Transports,
      });
    }
    return responses;
  }

  public async Task<CeremonyResponse<CredentialCreateOptions>> StartRegistrationAsync(
    SessionPrincipal principal,
    CancellationToken cancellationToken
  ) {
    var existing = await _passkeyRepository.GetAllAsync(principal.User.UserId, cancellationToken);
    var options = _webAuthnService.CreateRegistrationOptions(principal.User, existing);
    return new(_registrationChallenges.Add(options, BindingKey(principal)), options);
  }

  public async Task<ValidationErrors> ValidateRegistrationAsync(
    SessionPrincipal principal,
    RegistrationRequest request,
    CancellationToken cancellationToken
  ) {
    var errors = await _validator.ValidateAsync(request, cancellationToken);
    if (errors.Has("nickname")) return errors;

    await AddNicknameErrorAsync(errors, principal.User.UserId, request.Nickname, cancellationToken);
    return errors;
  }

  // Checks the new credential but stores nothing: it becomes a pending change. Null when it does not verify.
  public async Task<PendingChangeResponse?> FinishRegistrationAsync(
    SessionPrincipal principal,
    RegistrationRequest request,
    CancellationToken cancellationToken
  ) {
    var options = _registrationChallenges.Take(request.AttemptToken, BindingKey(principal));
    if (options is null) return null;
    var registration = await _webAuthnService.VerifyRegistrationAsync(request.Response, options, cancellationToken);
    if (registration is null) return null;
    return new(_changes.Add(new PendingAddition(registration, request.Nickname), BindingKey(principal)));
  }

  public async Task<(RemovalOutcome Outcome, PendingChangeResponse? Change)> StartRemovalAsync(
    SessionPrincipal principal,
    int passkeyId,
    CancellationToken cancellationToken
  ) {
    var passkeys = await _passkeyRepository.GetAllAsync(principal.User.UserId, cancellationToken);
    if (passkeys.All(passkey => passkey.PasskeyId != passkeyId)) return (RemovalOutcome.NotFound, null);
    if (passkeys.Count < 2) return (RemovalOutcome.LastPasskey, null);
    return (RemovalOutcome.Started, new(_changes.Add(new PendingRemoval(passkeyId), BindingKey(principal))));
  }

  // Assertion options for the passkeys already registered, with a challenge bound to the pending change.
  public async Task<CeremonyResponse<AssertionOptions>?> StartConfirmationAsync(
    SessionPrincipal principal,
    string changeToken,
    CancellationToken cancellationToken
  ) {
    if (_changes.Peek(changeToken, BindingKey(principal)) is null) return null;
    var passkeys = await _passkeyRepository.GetAllAsync(principal.User.UserId, cancellationToken);
    var options = _webAuthnService.CreateAssertionOptions(passkeys);
    return new(_confirmChallenges.Add(new(changeToken, options), BindingKey(principal)), options);
  }

  // True when the confirmation verified and the change was applied.
  public async Task<bool> ConfirmAsync(
    SessionPrincipal principal,
    ConfirmationRequest request,
    CancellationToken cancellationToken
  ) {
    var challenge = _confirmChallenges.Take(request.AttemptToken, BindingKey(principal));
    if (challenge is null) return false;
    if (!string.Equals(challenge.ChangeToken, request.ChangeToken, StringComparison.Ordinal)) return false;

    var confirming = await _passkeyRepository.GetByCredentialIdAsync(request.Response.RawId, cancellationToken);
    if (confirming is null) return false;
    var result = await _webAuthnService.VerifyAssertionAsync(
      request.Response,
      challenge.Options,
      confirming,
      principal.User,
      cancellationToken);
    if (result is null) return false;

    var change = _changes.Take(request.ChangeToken, BindingKey(principal));
    if (change is null) return false;

    await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
    await _passkeyRepository.RecordUseAsync(
      confirming.PasskeyId,
      result.SignCount,
      result.IsBackedUp,
      _clock.GetCurrentInstant(),
      cancellationToken);
    var applied = await ApplyAsync(principal.User.UserId, change, cancellationToken);
    if (!applied) return false;
    await transaction.CommitAsync(cancellationToken);
    return true;
  }

  public async Task<ValidationErrors> ValidateRenameAsync(
    SessionPrincipal principal,
    int passkeyId,
    RenameRequest request,
    CancellationToken cancellationToken
  ) {
    var errors = await _validator.ValidateAsync(request, cancellationToken);
    if (errors.Has("nickname")) return errors;
    var passkeys = await _passkeyRepository.GetAllAsync(principal.User.UserId, cancellationToken);
    if (passkeys.Any(passkey => passkey.PasskeyId != passkeyId && IsSameNickname(passkey.Nickname, request.Nickname)))
      errors.Add("nickname", "Nickname is already in use.");
    return errors;
  }

  public Task<bool> RenameAsync(
    SessionPrincipal principal,
    int passkeyId,
    RenameRequest request,
    CancellationToken cancellationToken
  )
    => _passkeyRepository.RenameAsync(principal.User.UserId, passkeyId, request.Nickname, cancellationToken);

  // A removal is re-checked here: the set may have changed since the change was started.
  private async Task<bool> ApplyAsync(int userId, PendingPasskeyChange change, CancellationToken cancellationToken) {
    switch (change) {
      case PendingAddition addition:
        var passkey = addition.Registration.ToNewPasskey(userId, addition.Nickname);
        await _passkeyRepository.CreateAsync(passkey, cancellationToken);
        return true;
      case PendingRemoval removal:
        var passkeys = await _passkeyRepository.GetAllAsync(userId, cancellationToken);
        return passkeys.Count > 1 && await _passkeyRepository.DeleteAsync(userId, removal.PasskeyId, cancellationToken);
      default:
        throw new InvalidOperationException($"Unknown passkey change {change.GetType().Name}.");
    }
  }

  private async Task AddNicknameErrorAsync(
    ValidationErrors errors,
    int userId,
    string nickname,
    CancellationToken cancellationToken
  ) {
    var passkeys = await _passkeyRepository.GetAllAsync(userId, cancellationToken);
    if (!passkeys.Any(passkey => IsSameNickname(passkey.Nickname, nickname))) return;

    errors.Add("nickname", "Nickname is already in use.");
  }

  private static bool IsSameNickname(string left, string right)
    => string.Equals(left, right, StringComparison.Ordinal);

  private static string BindingKey(SessionPrincipal principal)
    => principal.SessionId.ToString(CultureInfo.InvariantCulture);
}
