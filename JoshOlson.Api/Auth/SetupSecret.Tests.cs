using Xunit;

namespace JoshOlson.Api.Auth;

public class SetupSecretTests {
  [Fact]
  public void Matches_TheSameSecret_IsTrue()
    => Assert.True(SetupSecret.Matches("foo", "foo"));

  [Theory]
  [InlineData("foo", "bar")]
  [InlineData("foo", "fo")]
  [InlineData("foo", "fooo")]
  [InlineData("foo", "")]
  [InlineData("foo", "FOO")]
  public void Matches_AnythingElse_IsFalse(string configured, string given)
    => Assert.False(SetupSecret.Matches(configured, given));

  [Theory]
  [InlineData("")]
  [InlineData("foo")]
  public void Matches_WhenSetupIsOff_IsFalseEvenForAnEmptyGuess(string given)
    => Assert.False(SetupSecret.Matches("", given));
}
