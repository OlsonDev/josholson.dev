using Fido2NetLib;
using JoshOlson.Api.Common.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JoshOlson.Api.Auth;

/// <summary>
/// Enrolling a passkey with a one-time token: the first one, or a replacement when every passkey is lost. Anonymous,
/// since there is no passkey to sign in with yet.
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

  /// <summary>Starts enrolling: returns the options for the browser's WebAuthn API to create a passkey.</summary>
  /// <remarks>The answer for an unknown, used, or expired token is the same, so it says nothing about which.</remarks>
  /// <param name="request">The enrollment token.</param>
  /// <response code="200">The options for the browser's WebAuthn API, and the attempt token for them.</response>
  /// <response code="404">The token is unknown, used, or expired.</response>
  [HttpPost("options")]
  [ProducesResponseType<CeremonyResponse<CredentialCreateOptions>>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<CeremonyResponse<CredentialCreateOptions>>> Options(
    EnrollmentOptionsRequest request,
    CancellationToken cancellationToken
  ) {
    var started = await _enrollmentService.StartAsync(request.Token, cancellationToken);
    return started is null ? NotFound() : started;
  }

  /// <summary>Finishes enrolling: checks the credential, stores the passkey, and signs in with it.</summary>
  /// <param name="request">The enrollment token, and what the browser's WebAuthn API produced.</param>
  /// <response code="204">Enrolled. The session cookie is set, and there is no body.</response>
  /// <response code="400">The credential did not verify.</response>
  /// <response code="422">The nickname is not valid.</response>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status400BadRequest)]
  [ValidationProblemResponse]
  public async Task<IActionResult> Enroll(EnrollmentRequest request, CancellationToken cancellationToken) {
    var errors = await _enrollmentService.ValidateFinishAsync(request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

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
