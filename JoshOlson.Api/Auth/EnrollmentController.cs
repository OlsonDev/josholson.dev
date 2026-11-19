using Fido2NetLib;
using JoshOlson.Api.Common.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JoshOlson.Api.Auth;

/// <summary>
/// Registering the first passkey from the browser: setup, with the secret the API was started with, while no passkey
/// exists, then enrolling with the token it gives. Anonymous, since there is no passkey to sign in with yet.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("auth/enrollment")]
public sealed class EnrollmentController : ControllerBase {
  private readonly EnrollmentService _enrollmentService;
  private readonly AuthOptions _options;

  /// <summary>Takes what the actions use.</summary>
  public EnrollmentController(EnrollmentService enrollmentService, IOptions<AuthOptions> options) {
    _enrollmentService = enrollmentService;
    _options = options.Value;
  }

  /// <summary>Whether setup is open, so the admin page knows whether to offer it or a sign-in.</summary>
  [HttpGet]
  public async Task<EnrollmentStatusResponse> Status(CancellationToken cancellationToken)
    => await _enrollmentService.GetStatusAsync(cancellationToken);

  /// <summary>Starts setting up: trades the setup secret for a one-time token to enroll the first passkey with.</summary>
  /// <remarks>
  /// A wrong secret, setup being off, and setup being closed (a passkey exists) all answer the same, so a caller learns
  /// nothing about which.
  /// </remarks>
  /// <param name="request">The setup secret, and a name for the account if it has none yet.</param>
  /// <response code="200">The token to enroll with. It expires after 15 minutes.</response>
  /// <response code="404">The secret is wrong, or setup is off or closed.</response>
  /// <response code="422">The account needs a name, or the name is not valid.</response>
  [HttpPost("setup")]
  [ProducesResponseType<EnrollmentTokenResponse>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  [ValidationProblemResponse]
  public async Task<ActionResult<EnrollmentTokenResponse>> Setup(
    EnrollmentSetupRequest request,
    CancellationToken cancellationToken
  ) {
    if (!await _enrollmentService.AllowsSetupAsync(request.SetupSecret, cancellationToken)) return NotFound();
    var errors = await _enrollmentService.ValidateSetupAsync(request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    return new EnrollmentTokenResponse {
      Token = await _enrollmentService.CreateTokenAsync(request.DisplayName, cancellationToken),
    };
  }

  /// <summary>Starts enrolling: returns the options for the browser's WebAuthn API to create a passkey.</summary>
  /// <remarks>The answer for an unknown, used, or expired token is the same, so it says nothing about which.</remarks>
  /// <param name="request">The enrollment token, and what to call the new passkey.</param>
  /// <response code="200">The options for the browser's WebAuthn API, and the attempt token for them.</response>
  /// <response code="404">The token is unknown, used, or expired.</response>
  /// <response code="422">The nickname is not valid, or another passkey has it.</response>
  [HttpPost("options")]
  [ProducesResponseType<CeremonyResponse<CredentialCreateOptions>>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  [ValidationProblemResponse]
  public async Task<ActionResult<CeremonyResponse<CredentialCreateOptions>>> Options(
    EnrollmentOptionsRequest request,
    CancellationToken cancellationToken
  ) {
    var errors = await _enrollmentService.ValidateStartAsync(request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    var started = await _enrollmentService.StartAsync(request, cancellationToken);
    return started is null ? NotFound() : started;
  }

  /// <summary>Finishes enrolling: checks the credential, stores the passkey, and signs in with it.</summary>
  /// <param name="request">The enrollment token, and what the browser's WebAuthn API produced.</param>
  /// <response code="204">Enrolled. The session cookie is set, and there is no body.</response>
  /// <response code="400">The credential did not verify.</response>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Enroll(EnrollmentRequest request, CancellationToken cancellationToken) {
    var token = await _enrollmentService.FinishAsync(
      request,
      HttpContext.Connection.RemoteIpAddress,
      Request.Headers.UserAgent.ToString(),
      cancellationToken);
    if (token is null) return BadRequest();

    Response.SetSession(_options, token);
    return NoContent();
  }
}
