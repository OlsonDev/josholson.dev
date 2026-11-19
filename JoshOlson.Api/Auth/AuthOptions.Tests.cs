using System.ComponentModel.DataAnnotations;
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
  public void SetupSecret_IsOffByDefault_AndRefusesAShortOne() {
    Assert.Empty(new AuthOptions().SetupSecret);
    Assert.Empty(Validate(new AuthOptions()));
    Assert.Empty(Validate(new AuthOptions { SetupSecret = new('x', AuthOptions.MinimumSetupSecretLength) }));

    var tooShort = Assert.Single(Validate(new AuthOptions { SetupSecret = "short" }));
    Assert.Equal([nameof(AuthOptions.SetupSecret)], tooShort.MemberNames);
  }

  private static List<ValidationResult> Validate(AuthOptions options)
    => [.. options.Validate(new ValidationContext(options))];

  [Fact]
  public void Lifetimes_KeepTheIdleLifetimeWithinTheAbsoluteOne() {
    var options = new AuthOptions();

    Assert.Equal(TimeSpan.FromDays(7), options.SessionIdleLifetime);
    Assert.Equal(TimeSpan.FromDays(30), options.SessionAbsoluteLifetime);
    Assert.True(options.SessionIdleLifetime < options.SessionAbsoluteLifetime);
  }
}
