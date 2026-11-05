using Xunit;

namespace JoshOlson.Database.Locks;

public class ColumnTypeTests {
  [Theory]
  [InlineData("text", SeedType.Text, false)]
  [InlineData("boolean", SeedType.Boolean, false)]
  [InlineData("smallint", SeedType.SmallInt, false)]
  [InlineData("integer", SeedType.Integer, false)]
  [InlineData("bigint", SeedType.BigInt, false)]
  [InlineData("timestamptz", SeedType.TimestampTz, false)]
  [InlineData("bytea", SeedType.Bytea, false)]
  [InlineData("jsonb", SeedType.Jsonb, false)]
  [InlineData("inet", SeedType.Inet, false)]
  [InlineData("smallint[]", SeedType.SmallInt, true)]
  [InlineData("text[]", SeedType.Text, true)]
  public void Parse_KnownType_RoundTripsThroughToString(string text, SeedType element, bool isArray) {
    var type = ColumnType.Parse(text);

    Assert.Equal(element, type.Element);
    Assert.Equal(isArray, type.IsArray);
    Assert.Equal(text, type.ToString());
  }

  [Theory]
  [InlineData("")]
  [InlineData("[]")]
  [InlineData("int")]
  [InlineData("Text")]
  [InlineData("timestamp with time zone")]
  [InlineData("interval")]
  [InlineData("text[][]")]
  [InlineData("text []")]
  [InlineData("citext")]
  public void TryParse_UnsupportedType_ReturnsFalse(string text) {
    Assert.False(ColumnType.TryParse(text, out var type));
    Assert.Equal(default, type);
  }

  [Fact]
  public void Parse_UnsupportedType_ThrowsNamingIt() {
    var exception = Assert.Throws<SeedFormatException>(() => ColumnType.Parse("interval"));

    Assert.Contains("'interval'", exception.Message, StringComparison.Ordinal);
  }
}
