using System.Text.Json;
using Xunit;

namespace JoshOlson.Api.Common.Extensions;

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
  [InlineData("URL")]
  [InlineData("IDNumber")]
  [InlineData("TagID")]
  [InlineData("X")]
  public void ToCamelCasePath_MatchesTheJsonSerializerForAcronyms(string name)
    => Assert.Equal(JsonNamingPolicy.CamelCase.ConvertName(name), name.ToCamelCasePath());
}
