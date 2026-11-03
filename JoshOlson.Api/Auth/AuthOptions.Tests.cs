using Xunit;

namespace JoshOlson.Api.Auth;

public class AuthOptionsTests {
  [Fact]
  public void Defaults_SuitLocalDevelopmentAndTheSecureCookiePrefix() {
    var options = new AuthOptions();

    Assert.Equal("localhost", options.RelyingPartyId);
    Assert.Equal("http://localhost:5173", options.AllowedOrigin);
    // The prefix makes a browser refuse the cookie unless it is `Secure`, so a secure cookie keeps it.
    Assert.StartsWith("__Secure-", options.CookieName, StringComparison.Ordinal);
    Assert.True(options.CookieSecure);
  }

  [Fact]
  public void Lifetimes_KeepTheIdleLifetimeWithinTheAbsoluteOne() {
    var options = new AuthOptions();

    Assert.Equal(TimeSpan.FromDays(7), options.SessionIdleLifetime);
    Assert.Equal(TimeSpan.FromDays(30), options.SessionAbsoluteLifetime);
    Assert.True(options.SessionIdleLifetime < options.SessionAbsoluteLifetime);
  }
}
