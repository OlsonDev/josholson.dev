using Xunit;

namespace JoshOlson.Database.Schema;

public class ColumnTypeTests {
  [Theory]
  [InlineData("text", SeedType.Text, null, false)]
  [InlineData("boolean", SeedType.Boolean, null, false)]
  [InlineData("smallint", SeedType.SmallInt, null, false)]
  [InlineData("integer", SeedType.Integer, null, false)]
  [InlineData("bigint", SeedType.BigInt, null, false)]
  [InlineData("timestamptz", SeedType.TimestampTz, null, false)]
  [InlineData("bytea", SeedType.Bytea, null, false)]
  [InlineData("jsonb", SeedType.Jsonb, null, false)]
  [InlineData("inet", SeedType.Inet, null, false)]
  [InlineData("decimal", SeedType.Decimal, null, false)]
  [InlineData("decimal(9)", SeedType.Decimal, "9", false)]
  [InlineData("decimal(9, 6)", SeedType.Decimal, "9, 6", false)]
  [InlineData("decimal(9, 0)", SeedType.Decimal, "9, 0", false)]
  [InlineData("smallint[]", SeedType.SmallInt, null, true)]
  [InlineData("text[]", SeedType.Text, null, true)]
  [InlineData("timestamptz[]", SeedType.TimestampTz, null, true)]
  [InlineData("decimal(9, 6)[]", SeedType.Decimal, "9, 6", true)]
  public void Parse_BaseType_RoundTripsThroughToString(string text, SeedType seed, string? modifiers, bool isArray) {
    var type = ColumnType.Parse(text);

    Assert.Equal(seed, type.Seed);
    Assert.False(type.IsDomain);
    Assert.Equal(modifiers, type.Modifiers);
    Assert.Equal(isArray, type.IsArray);
    Assert.Equal(text, type.ToString());
  }

  [Theory]
  [InlineData("foo.bar", false)]
  [InlineData("foo.bar[]", true)]
  [InlineData("text.kebab_case", false)]
  public void Parse_Domain_HasNoSeedTypeOfItsOwn(string text, bool isArray) {
    var type = ColumnType.Parse(text);

    Assert.True(type.IsDomain);
    Assert.Null(type.Seed);
    Assert.Null(type.Modifiers);
    Assert.Equal(isArray, type.IsArray);
    Assert.Equal(text, type.ToString());
  }

  [Theory]
  [InlineData("")]
  [InlineData("[]")]
  [InlineData("int")]
  [InlineData("int4")]
  [InlineData("Text")]
  [InlineData("numeric")]
  [InlineData("numeric(9, 6)")]
  [InlineData("timestamp with time zone")]
  [InlineData("timestamptz(3)")]
  [InlineData("interval")]
  [InlineData("citext")]
  [InlineData("1")]
  [InlineData("text,boolean")]
  [InlineData("text[][]")]
  [InlineData("text[3]")]
  [InlineData("text[")]
  [InlineData("text []")]
  [InlineData("text(5)")]
  [InlineData("decimal(9,6)")]
  [InlineData("decimal(09)")]
  [InlineData("decimal(9, 6, 1)")]
  [InlineData("decimal(a)")]
  [InlineData("decimal()")]
  [InlineData("decimal(9, 6")]
  [InlineData("decimal(9, 6)x")]
  [InlineData("foo.")]
  [InlineData(".bar")]
  [InlineData("foo.bar.baz")]
  [InlineData("foo bar.baz")]
  [InlineData("foo.bar(3)")]
  [InlineData("foo.bar[][]")]
  public void TryParse_NotACanonicalSupportedType_ReturnsFalse(string text) {
    Assert.False(ColumnType.TryParse(text, out var type));
    Assert.Equal(default, type);
  }

  [Fact]
  public void Parse_UnsupportedType_ThrowsNamingIt() {
    var exception = Assert.Throws<SeedFormatException>(() => ColumnType.Parse("interval"));

    Assert.Contains("'interval'", exception.Message, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("foo.bar", true)]
  [InlineData("text.kebab_case", true)]
  [InlineData("foo", false)]
  [InlineData("foo.", false)]
  [InlineData("foo.bar.baz", false)]
  [InlineData("foo.bar[]", false)]
  [InlineData("foo.bar baz", false)]
  public void IsDomainName_NeedsASchemaAndAName(string name, bool expected)
    => Assert.Equal(expected, ColumnType.IsDomainName(name));
}
