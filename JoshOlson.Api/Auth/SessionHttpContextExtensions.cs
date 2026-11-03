namespace JoshOlson.Api.Auth;

public static class SessionHttpContextExtensions {
  extension(HttpContext httpContext) {
    // The signed-in session. Throws on an [AllowAnonymous] action.
    public SessionPrincipal GetSession()
      => SessionFilter.GetPrincipal(httpContext);
  }
}
