using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JoshOlson.Api.Auth;

[ApiController]
[Route("auth")]
public sealed class SignInController : ControllerBase {
  private readonly SignInService _signInService;
  private readonly SessionService _sessionService;
  private readonly AuthOptions _options;

  public SignInController(SignInService signInService, SessionService sessionService, IOptions<AuthOptions> options) {
    _signInService = signInService;
    _sessionService = sessionService;
    _options = options.Value;
  }

  [AllowAnonymous]
  [HttpPost("sign-in/options")]
  public CeremonyResponse<AssertionOptions> Options()
    => _signInService.Start();

  // 401 for any failure, and no reason, so a probe learns nothing.
  [AllowAnonymous]
  [HttpPost("sign-in")]
  public async Task<IActionResult> SignIn(SignInRequest request, CancellationToken cancellationToken) {
    var token = await _signInService.FinishAsync(
      request,
      HttpContext.Connection.RemoteIpAddress,
      Request.Headers.UserAgent.ToString(),
      cancellationToken);
    if (token is null) return Unauthorized();

    Response.SetSession(_options, token);
    return NoContent();
  }

  [AllowAnonymous]
  [HttpPost("sign-out")]
  public async Task<IActionResult> SignOut(CancellationToken cancellationToken) {
    var token = Request.GetSessionToken(_options);
    if (token is not null) await _sessionService.EndAsync(token, cancellationToken);
    Response.ClearSession(_options);
    return NoContent();
  }

  [HttpGet("me")]
  public CurrentUserResponse Me()
    => new() {
      UserId = HttpContext.GetSession().User.UserId,
      DisplayName = HttpContext.GetSession().User.DisplayName,
    };
}
