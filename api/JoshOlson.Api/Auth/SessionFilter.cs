using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace JoshOlson.Api.Auth;

// Every action needs a signed-in session unless it is marked `[AllowAnonymous]` (docs/security.md: authenticated is
// the default, public is the explicit exception). Registered as a global MVC filter by `AddAuth`. Without a valid
// session cookie the response is 401; with one, the user is `HttpContext.GetSession()`.
public sealed class SessionFilter : IAsyncAuthorizationFilter {
  private static readonly object _ItemKey = new();

  public async Task OnAuthorizationAsync(AuthorizationFilterContext context) {
    if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;

    var services = context.HttpContext.RequestServices;
    var options = services.GetRequiredService<IOptions<AuthOptions>>().Value;
    var token = context.HttpContext.Request.GetSessionToken(options);
    var sessionService = services.GetRequiredService<SessionService>();
    var principal = token is null
      ? null
      : await sessionService.AuthenticateAsync(token, context.HttpContext.RequestAborted);
    if (principal is null) {
      context.Result = new UnauthorizedResult();
      return;
    }
    context.HttpContext.Items[_ItemKey] = principal;
  }

  public static SessionPrincipal GetPrincipal(HttpContext httpContext)
    => httpContext.Items[_ItemKey] as SessionPrincipal
      ?? throw new InvalidOperationException("The action is [AllowAnonymous], so it has no session to read.");
}
