using JoshOlson.Database.Schema;
using JoshOlson.Database.Yaml;
using NodaTime;
using Xunit;

namespace JoshOlson.Database.Layers;

public class SeedValueReaderTests {
  [Fact]
  public void Read_PlainNullForms_AreNull() {
    foreach (var text in new[] { "~", "null", "" }) {
      Assert.Null(Problem(SeedType.Text, text, out var value, nullable: true));
      Assert.Null(value);
    }
  }

  [Fact]
  public void Read_QuotedNull_IsText() {
    Assert.Null(Problem(SeedType.Text, "\"null\"", out var value));
    Assert.Equal(new SeedValue.Text("null"), value);
  }

  [Fact]
  public void Read_NullInNotNullColumn_IsAProblem() {
    var problem = Problem(SeedType.Text, "~", out var value, nullable: false);

    Assert.Equal("is NULL, but the column is NOT NULL.", problem);
    Assert.Null(value);
  }

  [Fact]
  public void Read_Text_IsTheTextWhateverYamlWouldInfer() {
    Assert.Null(Problem(SeedType.Text, "1000", out var value));
    Assert.Equal(new SeedValue.Text("1000"), value);
  }

  [Theory]
  [InlineData("true", true)]
  [InlineData("false", false)]
  public void Read_Boolean_TakesTrueAndFalse(string text, bool expected) {
    Assert.Null(Problem(SeedType.Boolean, text, out var value));
    Assert.Equal(new SeedValue.Boolean(expected), value);
  }

  [Theory]
  [InlineData("True")]
  [InlineData("yes")]
  [InlineData("1")]
  public void Read_Boolean_RefusesOtherSpellings(string text)
    => Assert.NotNull(Problem(SeedType.Boolean, text, out _));

  [Theory]
  [InlineData(SeedType.SmallInt, "32767", 32767)]
  [InlineData(SeedType.SmallInt, "-32768", -32768)]
  [InlineData(SeedType.Integer, "2147483647", 2147483647)]
  [InlineData(SeedType.BigInt, "9223372036854775807", long.MaxValue)]
  [InlineData(SeedType.BigInt, "+5", 5)]
  public void Read_Integer_InRange_Parses(SeedType seed, string text, long expected) {
    Assert.Null(Problem(seed, text, out var value));
    Assert.Equal(new SeedValue.Integer(expected), value);
  }

  [Theory]
  [InlineData(SeedType.SmallInt, "32768")]
  [InlineData(SeedType.SmallInt, "-32769")]
  [InlineData(SeedType.Integer, "2147483648")]
  [InlineData(SeedType.BigInt, "9223372036854775808")]
  [InlineData(SeedType.Integer, "1.5")]
  [InlineData(SeedType.Integer, "abc")]
  [InlineData(SeedType.Integer, "1e3")]
  public void Read_Integer_OutOfRangeOrNotWhole_IsAProblem(SeedType seed, string text)
    => Assert.NotNull(Problem(seed, text, out _));

  [Fact]
  public void Read_Decimal_WithinPrecisionAndScale_Parses() {
    Assert.Null(Problem(SeedType.Decimal, "-123.456", out var value, modifiers: "9, 6"));
    Assert.Equal(new SeedValue.Decimal(-123.456m), value);
  }

  [Theory]
  [InlineData("1234.5", "5, 2")]
  [InlineData("1.234", "5, 2")]
  [InlineData("1.5", "5")]
  [InlineData("abc", null)]
  public void Read_Decimal_TooManyDigitsOrNotANumber_IsAProblem(string text, string? modifiers)
    => Assert.NotNull(Problem(SeedType.Decimal, text, out _, modifiers: modifiers));

  [Fact]
  public void Read_Decimal_LeadingZerosDoNotCountAsDigits() {
    Assert.Null(Problem(SeedType.Decimal, "007.5", out var value, modifiers: "3, 1"));
    Assert.Equal(new SeedValue.Decimal(7.5m), value);
  }

  [Fact]
  public void Read_RelativeTimestamp_IsAnOffsetInMicroseconds() {
    Assert.Null(Problem(SeedType.TimestampTz, "-1d 2h 0.000001s", out var value, policy: TimePolicy.Relative));
    Assert.Equal(new SeedValue.RelativeTime(-(86_400L + 7_200) * 1_000_000 - 1), value);
  }

  [Fact]
  public void Read_RelativeTimestamp_RefusesADate()
    => Assert.NotNull(Problem(SeedType.TimestampTz, "2026-01-02T03:04:05Z", out _, policy: TimePolicy.Relative));

  [Fact]
  public void Read_AbsoluteTimestamp_IsAnInstant() {
    Assert.Null(Problem(SeedType.TimestampTz, "2026-01-02T03:04:05.123456Z", out var value, policy: TimePolicy.Absolute));
    Assert.Equal(new SeedValue.AbsoluteTime(Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(123_456_000)), value);
  }

  [Theory]
  [InlineData("2026-01-02T03:04:05.1234567Z")]
  [InlineData("-3d")]
  [InlineData("2026-01-02")]
  public void Read_AbsoluteTimestamp_RefusesSubMicrosecondAndNonInstants(string text)
    => Assert.NotNull(Problem(SeedType.TimestampTz, text, out _, policy: TimePolicy.Absolute));

  [Fact]
  public void Read_Bytea_IsLowerCaseHexWithoutThePrefix() {
    Assert.Null(Problem(SeedType.Bytea, "\\xDEADbeef", out var value));
    Assert.Equal(new SeedValue.Bytes("deadbeef"), value);
  }

  [Theory]
  [InlineData("deadbeef")]
  [InlineData("\\xabc")]
  [InlineData("\\xzz")]
  public void Read_Bytea_RefusesAnythingButEvenHexAfterThePrefix(string text)
    => Assert.NotNull(Problem(SeedType.Bytea, text, out _));

  [Theory]
  [InlineData("192.168.0.1", "192.168.0.1")]
  [InlineData("10.0.0.0/8", "10.0.0.0/8")]
  [InlineData("::1", "::1")]
  [InlineData("2001:DB8::/32", "2001:db8::/32")]
  [InlineData("10.0.0.1/32", "10.0.0.1")]
  [InlineData("::1/128", "::1")]
  public void Read_Inet_IsTheAddressAndOptionalPrefix(string text, string expected) {
    Assert.Null(Problem(SeedType.Inet, text, out var value));
    Assert.Equal(new SeedValue.Inet(expected), value);
  }

  [Theory]
  [InlineData("1")]
  [InlineData("foo")]
  [InlineData("10.0.0.0/33")]
  [InlineData("::1/129")]
  [InlineData("10.0.0.0/8/8")]
  [InlineData("10.0.0.0/-1")]
  public void Read_Inet_RefusesAnythingElse(string text)
    => Assert.NotNull(Problem(SeedType.Inet, text, out _));

  [Fact]
  public void Read_Jsonb_IsNativeYamlWrittenAsJsonWithItsKeysInPostgresOrder() {
    var problem = Problem(SeedType.Jsonb, "{ foo: 1, bar: [true, ~, \"2\", 1.5, baz], qux: { quux: \"null\" } }", out var value);

    Assert.Null(problem);
    Assert.Equal(new SeedValue.Json("""{"bar":[true,null,"2",1.5,"baz"],"foo":1,"qux":{"quux":"null"}}"""), value);
  }

  [Fact]
  public void Read_Array_ReadsEachItemAsTheBaseType() {
    Assert.Null(Problem(SeedType.SmallInt, "[1, ~, 3]", out var value, isArray: true));

    var array = Assert.IsType<SeedValue.Array>(value);
    Assert.Equal([new SeedValue.Integer(1), null, new SeedValue.Integer(3)], array.Items);
  }

  [Fact]
  public void Read_Array_NamesTheBadItem() {
    var problem = Problem(SeedType.SmallInt, "[1, 99999]", out var value, isArray: true);

    Assert.Equal("item 2 is 99999, outside the range of smallint.", problem);
    Assert.Null(value);
  }

  [Fact]
  public void Read_Array_NeedsAList()
    => Assert.NotNull(Problem(SeedType.SmallInt, "1", out _, isArray: true));

  [Fact]
  public void Read_Scalar_RefusesAList()
    => Assert.NotNull(Problem(SeedType.SmallInt, "[1]", out _));

  private static string? Problem(
    SeedType seed,
    string yaml,
    out SeedValue? value,
    bool nullable = true,
    string? modifiers = null,
    bool isArray = false,
    TimePolicy? policy = null
  ) {
    var column = new Column {
      Name = "foo",
      Type = "text",
      DeclaredNullable = nullable,
      Nullable = nullable,
      GeneratedAs = null,
      GeneratedExpression = null,
      DefaultExpression = null,
      Policy = policy,
      Comment = null,
    };
    var node = YamlNodes.Find(YamlNodes.LoadRoot("test", $"v: {yaml}"), "v")!;
    return SeedValueReader.Read(new(column, new(seed, isArray, modifiers)), node, out value);
  }
}
