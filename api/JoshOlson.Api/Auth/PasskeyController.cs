using Fido2NetLib;
using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Auth;

[ApiController]
[Route("auth/passkeys")]
public sealed class PasskeyController : ControllerBase {
  private readonly PasskeyService _passkeyService;

  public PasskeyController(PasskeyService passkeyService)
    => _passkeyService = passkeyService;

  [HttpGet]
  public async Task<List<PasskeyResponse>> GetAll(CancellationToken cancellationToken)
    => await _passkeyService.GetAllAsync(HttpContext.GetSession().User.UserId, cancellationToken);

  [HttpPost("registration/options")]
  public async Task<CeremonyResponse<CredentialCreateOptions>> RegistrationOptions(CancellationToken cancellationToken)
    => await _passkeyService.StartRegistrationAsync(HttpContext.GetSession(), cancellationToken);

  // Checks the new credential and holds it as a pending change. Nothing is stored until it is confirmed.
  [HttpPost("registration")]
  public async Task<ActionResult<PendingChangeResponse>> Register(
    RegistrationRequest request,
    CancellationToken cancellationToken
  ) {
    var errors = await _passkeyService.ValidateRegistrationAsync(HttpContext.GetSession(), request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    var change = await _passkeyService.FinishRegistrationAsync(HttpContext.GetSession(), request, cancellationToken);
    return change is null ? BadRequest() : change;
  }

  [HttpPost("{passkeyId:int}/removal")]
  public async Task<ActionResult<PendingChangeResponse>> Remove(int passkeyId, CancellationToken cancellationToken) {
    var principal = HttpContext.GetSession();
    var (outcome, change) = await _passkeyService.StartRemovalAsync(principal, passkeyId, cancellationToken);
    return outcome switch {
      RemovalOutcome.Started => change!,
      RemovalOutcome.NotFound => NotFound(),
      _ => Conflict(),
    };
  }

  [HttpPost("confirmation/options")]
  public async Task<ActionResult<CeremonyResponse<AssertionOptions>>> ConfirmationOptions(
    ConfirmationOptionsRequest request,
    CancellationToken cancellationToken
  ) {
    var principal = HttpContext.GetSession();
    var started = await _passkeyService.StartConfirmationAsync(principal, request.ChangeToken, cancellationToken);
    return started is null ? NotFound() : started;
  }

  [HttpPost("confirmation")]
  public async Task<IActionResult> Confirm(ConfirmationRequest request, CancellationToken cancellationToken) {
    var confirmed = await _passkeyService.ConfirmAsync(HttpContext.GetSession(), request, cancellationToken);
    return confirmed ? NoContent() : BadRequest();
  }

  [HttpPut("{passkeyId:int}")]
  public async Task<IActionResult> Rename(int passkeyId, RenameRequest request, CancellationToken cancellationToken) {
    var principal = HttpContext.GetSession();
    var errors = await _passkeyService.ValidateRenameAsync(principal, passkeyId, request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    return await _passkeyService.RenameAsync(HttpContext.GetSession(), passkeyId, request, cancellationToken)
      ? NoContent()
      : NotFound();
  }
}
