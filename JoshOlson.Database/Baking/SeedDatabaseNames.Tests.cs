using Xunit;

namespace JoshOlson.Database.Baking;

public sealed class SeedDatabaseNamesTests {
  [Fact]
  public void Test_PlainId_IsPrefixed()
    => Assert.Equal("seed_test_42", SeedDatabaseNames.Test("42"));

  [Theory]
  [InlineData("")]
  [InlineData("Foo")]
  [InlineData("foo bar")]
  [InlineData("foo_bar")]
  [InlineData("foo\"; DROP DATABASE bar; --")]
  public void Test_NotAnId_Throws(string id)
    => _ = Assert.Throws<SeedFormatException>(() => SeedDatabaseNames.Test(id));

  [Fact]
  public void Test_IdLongerThanForty_Throws()
    => _ = Assert.Throws<SeedFormatException>(() => SeedDatabaseNames.Test(new string('a', 41)));

  [Fact]
  public void Scratch_Purpose_IsInANameThatIsNewEachTimeAndASeedToolName() {
    var first = SeedDatabaseNames.Scratch("foo");
    var second = SeedDatabaseNames.Scratch("foo");

    Assert.StartsWith("seed_foo_", first, StringComparison.Ordinal);
    Assert.NotEqual(first, second);
    Assert.Equal($"\"{first}\"", SeedDatabaseNames.Quote(first));
    Assert.False(SeedDatabaseNames.IsTemplate(first));
  }

  [Fact]
  public void IsTemplate_SchemaAndLayerNamesOnly() {
    Assert.True(SeedDatabaseNames.IsTemplate(SeedDatabaseNames.Schema(1, "foo")));
    Assert.True(SeedDatabaseNames.IsTemplate(SeedDatabaseNames.Layer("foo")));
    Assert.False(SeedDatabaseNames.IsTemplate(SeedDatabaseNames.Test("foo")));
    Assert.False(SeedDatabaseNames.IsTemplate("josholson"));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("foo_bar2")]
  public void QuoteDevelopment_PlainName_IsQuoted(string name)
    => Assert.Equal($"\"{name}\"", SeedDatabaseNames.QuoteDevelopment(name));

  [Theory]
  [InlineData("postgres")]
  [InlineData("template0")]
  [InlineData("template1")]
  [InlineData("Foo")]
  [InlineData("foo-bar")]
  [InlineData("foo\"; DROP DATABASE bar; --")]
  public void QuoteDevelopment_ServerDatabaseOrOddName_Throws(string name)
    => _ = Assert.Throws<SeedFormatException>(() => SeedDatabaseNames.QuoteDevelopment(name));
}
