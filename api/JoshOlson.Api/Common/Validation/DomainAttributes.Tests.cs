using System.ComponentModel.DataAnnotations;
using Xunit;

namespace JoshOlson.Api.Common.Validation;

public class DomainAttributesTests {
  [Theory]
  [InlineData("foo", true)]
  [InlineData("foo bar", true)]
  [InlineData("a", true)]
  [InlineData("", false)]
  [InlineData(" foo", false)]
  [InlineData("foo ", false)]
  [InlineData("foo\n", false)]
  [InlineData("   ", false)]
  public void Trimmed_MatchesTheDomain(string value, bool expected)
    => Assert.Equal(expected, new TrimmedAttribute().IsValid(value));

  [Theory]
  [InlineData("foo", true)]
  [InlineData("foo-bar-2", true)]
  [InlineData("", false)]
  [InlineData("Foo", false)]
  [InlineData("foo--bar", false)]
  [InlineData("-foo", false)]
  [InlineData("foo-", false)]
  [InlineData("foo bar", false)]
  [InlineData("foo\n", false)]
  public void KebabCase_MatchesTheDomain(string value, bool expected)
    => Assert.Equal(expected, new KebabCaseAttribute().IsValid(value));

  [Theory]
  [InlineData("#a1b2c3", true)]
  [InlineData("", false)]
  [InlineData("#A1B2C3", false)]
  [InlineData("a1b2c3", false)]
  [InlineData("#a1b2c", false)]
  [InlineData("#a1b2c3\n", false)]
  public void HexColor_MatchesTheDomain(string value, bool expected)
    => Assert.Equal(expected, new HexColorAttribute().IsValid(value));

  [Fact]
  public void Null_PassesSoThatRequiredDecidesAlone()
    => Assert.True(new KebabCaseAttribute().IsValid(null));

  [Fact]
  public void Message_NamesTheField() {
    var attribute = new KebabCaseAttribute();
    Assert.StartsWith("Slug must", attribute.FormatErrorMessage("Slug"));
  }
}
