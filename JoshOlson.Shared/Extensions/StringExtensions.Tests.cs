using System.Text.Json;
using Xunit;

namespace JoshOlson.Shared.Extensions;

public class StringExtensionsTests {
  [Theory]
  [InlineData("", "")]
  [InlineData("Foo", "foo")]
  [InlineData("foo", "foo")]
  [InlineData("FooBar", "fooBar")]
  [InlineData("Foo.Bar", "foo.bar")]
  [InlineData("Foo[0]", "foo[0]")]
  [InlineData("Foo[0].Bar", "foo[0].bar")]
  [InlineData("Foo.Bar[12].Baz.Qux[3]", "foo.bar[12].baz.qux[3]")]
  [InlineData("Matrix[0][1]", "matrix[0][1]")]
  [InlineData("Foo[Key]", "foo[Key]")]
  public void ToCamelCasePath_CamelCasesEachSegment(string path, string expected)
    => Assert.Equal(expected, path.ToCamelCasePath());

  [Theory]
  [InlineData("", "")]
  [InlineData("Foo", "foo")]
  [InlineData("foo", "foo")]
  [InlineData("FooBar", "foo-bar")]
  [InlineData("FooBarBaz", "foo-bar-baz")]
  [InlineData("Foo2Bar", "foo2-bar")]
  [InlineData("fooBar", "foo-bar")]
  public void ToKebabCase_PutsAHyphenBeforeEachCapital(string name, string expected)
    => Assert.Equal(expected, name.ToKebabCase());

  [Theory]
  [InlineData("URL")]
  [InlineData("IDNumber")]
  [InlineData("TagID")]
  [InlineData("X")]
  public void ToCamelCasePath_MatchesTheJsonSerializerForAcronyms(string name)
    => Assert.Equal(JsonNamingPolicy.CamelCase.ConvertName(name), name.ToCamelCasePath());

  [Theory]
  [InlineData(null, null)]
  [InlineData("", null)]
  [InlineData("  \t\n", null)]
  [InlineData("foo", "foo")]
  [InlineData("  foo bar \n", "foo bar")]
  public void TrimToNull_TrimsAndTurnsNothingIntoNull(string? value, string? expected)
    => Assert.Equal(expected, value.TrimToNull());

  [Theory]
  [InlineData("", 3, "")]
  [InlineData("foo", 3, "foo")]
  [InlineData("foobar", 3, "foo")]
  [InlineData("foo", 0, "")]
  public void Truncate_KeepsAtMostTheLength(string value, int maxLength, string expected)
    => Assert.Equal(expected, value.Truncate(maxLength));

  [Theory]
  [InlineData("foo")]
  [InlineData("foo-bar")]
  [InlineData("foo-bar-42")]
  [InlineData("2foo")]
  [InlineData("f")]
  public void IsKebabCase_LowercaseWordsJoinedByHyphens_ReturnsTrue(string value)
    => Assert.True(value.IsKebabCase());

  [Theory]
  [InlineData("")]
  [InlineData("Foo")]
  [InlineData("foo_bar")]
  [InlineData("foo--bar")]
  [InlineData("-foo")]
  [InlineData("foo-")]
  [InlineData("foo.bar")]
  [InlineData("foo bar")]
  [InlineData("foo\n")]
  [InlineData("fóo")]
  public void IsKebabCase_Otherwise_ReturnsFalse(string value)
    => Assert.False(value.IsKebabCase());
}
