using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace JoshOlson.Api.Auth;

// CSRF protection for the API itself (ADR 0027, direct browser-to-API calls), because SvelteKit's own check covers only
// its form posts. A request that changes state must carry the allowed `Origin`, and a body must be JSON, which forces a
// preflight.
public sealed class OriginCheckMiddleware {
  private static readonly string[] _SafeMethods = [
    HttpMethods.Get,
    HttpMethods.Head,
    HttpMethods.Options,
  ];

  private readonly RequestDelegate _next;
  private readonly string _allowedOrigin;

  public OriginCheckMiddleware(RequestDelegate next, IOptions<AuthOptions> options) {
    _next = next;
    _allowedOrigin = options.Value.AllowedOrigin;
  }

  public async Task InvokeAsync(HttpContext context) {
    var request = context.Request;
    if (_SafeMethods.Any(method => HttpMethods.Equals(request.Method, method))) {
      await _next(context);
      return;
    }

    if (!string.Equals(request.Headers.Origin.ToString(), _allowedOrigin, StringComparison.Ordinal)) {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      return;
    }
    var hasBody = request.ContentLength > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding);
    if (hasBody && !request.HasJsonContentType()) {
      context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
      return;
    }
    await _next(context);
  }
}
