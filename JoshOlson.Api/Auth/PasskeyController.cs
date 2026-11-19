using Fido2NetLib;
using JoshOlson.Api.Common.Contract;
using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Auth;

/// <summary>
/// The signed-in user's passkeys: list, add, remove, and rename. Adding or removing one waits for a passkey that is
/// already registered to confirm it.
/// </summary>
[ApiController]
[Route("auth/passkeys")]
public sealed class PasskeyController : ControllerBase {
  private readonly PasskeyService _passkeyService;

  /// <summary>Takes the service the actions call.</summary>
  public PasskeyController(PasskeyService passkeyService)
    => _passkeyService = passkeyService;

  /// <summary>Lists the signed-in user's passkeys.</summary>
  [HttpGet]
  public async Task<List<PasskeyResponse>> GetAll(CancellationToken cancellationToken)
    => await _passkeyService.GetAllAsync(HttpContext.GetSession().User.UserId, cancellationToken);

  /// <summary>Starts adding a passkey: returns the options for the browser's WebAuthn API to create one.</summary>
  /// <param name="request">What to call the new passkey.</param>
  /// <response code="200">The options for the browser's WebAuthn API, and the attempt token for them.</response>
  /// <response code="422">The nickname is not valid, or another passkey has it.</response>
  [HttpPost("registration/options")]
  [ProducesResponseType<CeremonyResponse<CredentialCreateOptions>>(StatusCodes.Status200OK)]
  [ValidationProblemResponse]
  public async Task<ActionResult<CeremonyResponse<CredentialCreateOptions>>> RegistrationOptions(
    RegistrationOptionsRequest request,
    CancellationToken cancellationToken
  ) {
    var principal = HttpContext.GetSession();
    var errors = await _passkeyService.ValidateStartRegistrationAsync(principal, request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    return await _passkeyService.StartRegistrationAsync(principal, request, cancellationToken);
  }

  /// <summary>
  /// Checks the new credential and holds it as a pending change. Nothing is stored until a registered passkey
  /// confirms it.
  /// </summary>
  /// <param name="request">What the browser's WebAuthn API produced for the registration options.</param>
  /// <response code="200">The pending change to confirm.</response>
  /// <response code="400">The credential did not verify.</response>
  [HttpPost("registration")]
  [ProducesResponseType<PendingChangeResponse>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PendingChangeResponse>> Register(
    RegistrationRequest request,
    CancellationToken cancellationToken
  ) {
    var change = await _passkeyService.FinishRegistrationAsync(HttpContext.GetSession(), request, cancellationToken);
    return change is null ? BadRequest() : change;
  }

  /// <summary>
  /// Starts removing a passkey: holds the removal as a pending change that a registered passkey must confirm.
  /// </summary>
  /// <param name="passkeyId">The ID of the passkey to remove.</param>
  /// <response code="200">The pending change to confirm.</response>
  /// <response code="404">The user has no passkey with that ID.</response>
  /// <response code="409">It is the user's only passkey, and removing it would lock them out.</response>
  [HttpPost("{passkeyId:int}/removal")]
  [ProducesResponseType<PendingChangeResponse>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  [ProblemResponse(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<PendingChangeResponse>> Remove(int passkeyId, CancellationToken cancellationToken) {
    var principal = HttpContext.GetSession();
    var result = await _passkeyService.StartRemovalAsync(principal, passkeyId, cancellationToken);
    return result switch {
      PendingChangeResponse change => change,
      PasskeyNotFound => NotFound(),
      LastPasskey => Conflict(),
    };
  }

  /// <summary>Returns the options for confirming a pending change with a passkey that is already registered.</summary>
  /// <param name="request">The pending change to confirm.</param>
  /// <response code="200">The options for the browser's WebAuthn API, and the attempt token for them.</response>
  /// <response code="404">There is no such pending change for this user.</response>
  [HttpPost("confirmation/options")]
  [ProducesResponseType<CeremonyResponse<AssertionOptions>>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<CeremonyResponse<AssertionOptions>>> ConfirmationOptions(
    ConfirmationOptionsRequest request,
    CancellationToken cancellationToken
  ) {
    var principal = HttpContext.GetSession();
    var started = await _passkeyService.StartConfirmationAsync(principal, request.ChangeToken, cancellationToken);
    return started is null ? NotFound() : started;
  }

  /// <summary>Confirms a pending change with a registered passkey's signed assertion, and applies it.</summary>
  /// <param name="request">What the browser's WebAuthn API produced for the confirmation options.</param>
  /// <response code="204">Applied.</response>
  /// <response code="400">The assertion did not verify, or the pending change is gone.</response>
  [HttpPost("confirmation")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Confirm(ConfirmationRequest request, CancellationToken cancellationToken) {
    var confirmed = await _passkeyService.ConfirmAsync(HttpContext.GetSession(), request, cancellationToken);
    return confirmed ? NoContent() : BadRequest();
  }

  /// <summary>Renames a passkey.</summary>
  /// <param name="passkeyId">The ID of the passkey.</param>
  /// <param name="request">The new name.</param>
  /// <response code="204">Renamed.</response>
  /// <response code="404">The user has no passkey with that ID.</response>
  /// <response code="422">The nickname is not valid.</response>
  [HttpPut("{passkeyId:int}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  [ValidationProblemResponse]
  public async Task<IActionResult> Rename(int passkeyId, RenameRequest request, CancellationToken cancellationToken) {
    var principal = HttpContext.GetSession();
    var errors = await _passkeyService.ValidateRenameAsync(principal, passkeyId, request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    return await _passkeyService.RenameAsync(HttpContext.GetSession(), passkeyId, request, cancellationToken)
      ? NoContent()
      : NotFound();
  }
}
