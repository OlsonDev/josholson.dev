using System.Globalization;
using Xunit;

namespace JoshOlson.Shared.Extensions;

public class NumberExtensionsTests {
  [Theory]
  [InlineData(0, "0")]
  [InlineData(42, "42")]
  [InlineData(-1_000, "-1000")]
  [InlineData(int.MaxValue, "2147483647")]
  public void ToInvariantString_Int_WritesPlainDigits(int value, string expected)
    => Assert.Equal(expected, value.ToInvariantString());

  [Fact]
  public void ToInvariantString_Long_WritesPlainDigits() {
    Assert.Equal("-9223372036854775808", long.MinValue.ToInvariantString());
    Assert.Equal("9223372036854775807", long.MaxValue.ToInvariantString());
  }

  [Fact]
  public void ToInvariantString_IgnoresTheCurrentCulture() {
    var original = CultureInfo.CurrentCulture;
    try {
      // Arabic digits and a Unicode minus sign, the kind of culture that would change implicit formatting.
      CultureInfo.CurrentCulture = new("ar-SA");
      Assert.Equal("-12345", (-12_345).ToInvariantString());
    } finally {
      CultureInfo.CurrentCulture = original;
    }
  }
}
