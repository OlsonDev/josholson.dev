using Xunit;

namespace JoshOlson.Api.Auth;

public class SessionCookieTests {
  private static readonly AuthOptions _Options = new() {
    CookieName = "foo",
    CookieDomain = ".bar.example",
  };

  private static string SetCookieOf(HttpContext context)
    => context.Response.Headers.SetCookie.ToString();

  [Fact]
  public void SetSession_WritesASecureHttpOnlyLaxCookieOnTheParentDomain() {
    var context = new DefaultHttpContext();

    context.Response.SetSession(_Options, "baz");

    var header = SetCookieOf(context);
    Assert.StartsWith("foo=baz", header, StringComparison.Ordinal);
    string[] attributes = [
      "domain=.bar.example",
      "path=/",
      "secure",
      "samesite=lax",
      "httponly",
      "max-age=",
    ];
    foreach (var attribute in attributes) {
      Assert.Contains(attribute, header, StringComparison.OrdinalIgnoreCase);
    }
  }

  [Fact]
  public void SetSession_OnLocalhost_HasNoDomainAndNoSecureFlag() {
    var context = new DefaultHttpContext();
    var options = new AuthOptions {
      CookieName = "foo",
      CookieDomain = "",
      CookieSecure = false,
    };

    context.Response.SetSession(options, "baz");

    var header = SetCookieOf(context);
    Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("secure", header, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ClearSession_ExpiresTheCookieOnTheSameDomain() {
    var context = new DefaultHttpContext();

    context.Response.ClearSession(_Options);

    var header = SetCookieOf(context);
    Assert.StartsWith("foo=;", header, StringComparison.Ordinal);
    Assert.Contains("expires=Thu, 01 Jan 1970", header, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("domain=.bar.example", header, StringComparison.OrdinalIgnoreCase);
  }

  [Theory]
  [InlineData("foo=baz", "baz")]
  [InlineData("qux=1; foo=baz", "baz")]
  [InlineData("foo=", null)]
  [InlineData("qux=1", null)]
  public void GetSessionToken_ReadsTheCookieByName(string cookieHeader, string? expected) {
    var context = new DefaultHttpContext();
    context.Request.Headers.Cookie = cookieHeader;

    Assert.Equal(expected, context.Request.GetSessionToken(_Options));
  }
}
