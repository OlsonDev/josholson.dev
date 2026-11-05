using Xunit;

namespace JoshOlson.Shared.Extensions;

public class EnumExtensionsTests {
  public enum Foo {
    Bar = 1,
    BazQux = 2,
    Quux2Corge = 3,
  }

  [Theory]
  [InlineData(Foo.Bar, "bar")]
  [InlineData(Foo.BazQux, "baz-qux")]
  [InlineData(Foo.Quux2Corge, "quux2-corge")]
  public void ToKebabCase_IsTheMemberNameInKebabCase(Foo value, string expected)
    => Assert.Equal(expected, value.ToKebabCase());

  [Fact]
  public void ToKebabCase_ValueThatIsNotAMember_Throws()
    => Assert.Throws<ArgumentOutOfRangeException>(() => ((Foo)99).ToKebabCase());

  [Theory]
  [InlineData("bar", Foo.Bar)]
  [InlineData("baz-qux", Foo.BazQux)]
  [InlineData("quux2-corge", Foo.Quux2Corge)]
  public void TryParseKebabCase_WhatToKebabCaseWrites_IsParsedBack(string text, Foo expected) {
    Assert.True(Enum.TryParseKebabCase<Foo>(text, out var value));
    Assert.Equal(expected, value);
  }

  [Theory]
  [InlineData("")]
  [InlineData("Bar")]
  [InlineData("BAR")]
  [InlineData("bazqux")]
  [InlineData("baz-Qux")]
  [InlineData("baz--qux")]
  [InlineData("-baz-qux")]
  [InlineData("baz-qux-")]
  [InlineData("BazQux")]
  [InlineData("baz_qux")]
  [InlineData("1")]
  [InlineData("99")]
  [InlineData("bar,baz-qux")]
  [InlineData("bar ")]
  [InlineData("nope")]
  public void TryParseKebabCase_AnythingElse_IsRefused(string text) {
    Assert.False(Enum.TryParseKebabCase<Foo>(text, out var value));
    Assert.Equal(default, value);
  }
}
