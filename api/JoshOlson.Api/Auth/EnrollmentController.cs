using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JoshOlson.Api.Auth;

[AllowAnonymous]
[ApiController]
[Route("auth/enrollment")]
public sealed class EnrollmentController : ControllerBase {
  private readonly EnrollmentService _enrollmentService;
  private readonly AuthOptions _options;

  public EnrollmentController(EnrollmentService enrollmentService, IOptions<AuthOptions> options) {
    _enrollmentService = enrollmentService;
    _options = options.Value;
  }

  // 404 for an unknown, used, or expired token: it says nothing about which.
  [HttpPost("options")]
  public async Task<ActionResult<CeremonyResponse<CredentialCreateOptions>>> Options(
    EnrollmentOptionsRequest request,
    CancellationToken cancellationToken
  ) {
    var started = await _enrollmentService.StartAsync(request.Token, cancellationToken);
    return started is null ? NotFound() : started;
  }

  [HttpPost]
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
