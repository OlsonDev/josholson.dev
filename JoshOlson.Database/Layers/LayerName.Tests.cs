using Xunit;

namespace JoshOlson.Database.Layers;

public class LayerNameTests {
  [Theory]
  [InlineData("foo")]
  [InlineData("foo-bar")]
  [InlineData("foo2")]
  [InlineData("f")]
  public void IsValid_KebabCase_ReturnsTrue(string name)
    => Assert.True(LayerName.IsValid(name));

  [Theory]
  [InlineData("")]
  [InlineData("Foo")]
  [InlineData("foo_bar")]
  [InlineData("foo--bar")]
  [InlineData("-foo")]
  [InlineData("foo-")]
  [InlineData("2foo")]
  [InlineData("foo.bar")]
  [InlineData("foo bar")]
  public void IsValid_Otherwise_ReturnsFalse(string name)
    => Assert.False(LayerName.IsValid(name));

  [Fact]
  public void IsValid_LongerThanForty_ReturnsFalse() {
    Assert.True(LayerName.IsValid(new('a', 40)));
    Assert.False(LayerName.IsValid(new('a', 41)));
  }
}
