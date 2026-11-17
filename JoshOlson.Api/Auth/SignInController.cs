using Fido2NetLib;
using JoshOlson.Api.Common.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JoshOlson.Api.Auth;

/// <summary>Signing in with a passkey, signing out, and who is signed in.</summary>
[ApiController]
[Route("auth")]
public sealed class SignInController : ControllerBase {
  private readonly SignInService _signInService;
  private readonly SessionService _sessionService;
  private readonly AuthOptions _options;

  /// <summary>Takes what the actions use.</summary>
  public SignInController(SignInService signInService, SessionService sessionService, IOptions<AuthOptions> options) {
    _signInService = signInService;
    _sessionService = sessionService;
    _options = options.Value;
  }

  /// <summary>Starts signing in: returns the options for the browser's WebAuthn API to sign with a passkey.</summary>
  [AllowAnonymous]
  [HttpPost("sign-in/options")]
  public CeremonyResponse<AssertionOptions> Options()
    => _signInService.Start();

  /// <summary>Checks the signed assertion and, if it is good, signs in.</summary>
  /// <remarks>Every failure is the same 401, with no reason, so a probe learns nothing.</remarks>
  /// <param name="request">What the browser's WebAuthn API produced for the sign-in options.</param>
  /// <response code="204">Signed in. The session cookie is set, and there is no body.</response>
  /// <response code="401">The assertion did not verify.</response>
  [AllowAnonymous]
  [HttpPost("sign-in")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status401Unauthorized)]
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

  /// <summary>Ends the session and clears its cookie. Signing out with no session is not an error.</summary>
  /// <response code="204">Signed out.</response>
  [AllowAnonymous]
  [HttpPost("sign-out")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public async Task<IActionResult> SignOut(CancellationToken cancellationToken) {
    var token = Request.GetSessionToken(_options);
    if (token is not null) await _sessionService.EndAsync(token, cancellationToken);
    Response.ClearSession(_options);
    return NoContent();
  }

  /// <summary>Gets the signed-in user.</summary>
  [HttpGet("me")]
  public CurrentUserResponse Me()
    => new() {
      UserId = HttpContext.GetSession().User.UserId,
      DisplayName = HttpContext.GetSession().User.DisplayName,
    };
}
