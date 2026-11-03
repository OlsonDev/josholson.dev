namespace JoshOlson.Api.Auth;

// The session cookie (ADR 0027, passkey auth and direct browser-to-API calls): `HttpOnly`, `SameSite=Lax`, and on the
// parent domain so the browser sends it to both `www` and `api`. The API owns it. The server enforces the idle and
// absolute limits; the cookie just outlives them.
public static class SessionCookie {
  extension(HttpResponse response) {
    public void SetSession(AuthOptions options, string token)
      => response.Cookies.Append(
        options.CookieName,
        token,
        CreateCookieOptions(options, options.SessionAbsoluteLifetime));

    public void ClearSession(AuthOptions options)
      => response.Cookies.Delete(options.CookieName, CreateCookieOptions(options, null));
  }

  extension(HttpRequest request) {
    public string? GetSessionToken(AuthOptions options)
      => request.Cookies.TryGetValue(options.CookieName, out var token) && !string.IsNullOrEmpty(token) ? token : null;
  }

  private static CookieOptions CreateCookieOptions(AuthOptions options, TimeSpan? maxAge)
    => new() {
      HttpOnly = true,
      Secure = options.CookieSecure,
      SameSite = SameSiteMode.Lax,
      Path = "/",
      Domain = string.IsNullOrEmpty(options.CookieDomain) ? null : options.CookieDomain,
      MaxAge = maxAge,
    };
}
