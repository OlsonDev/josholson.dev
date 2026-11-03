using System.Text.Json;
using Xunit;

namespace JoshOlson.Api.Common.Binding;

public class TrimmingStringJsonConverterTests {
  private static readonly JsonSerializerOptions _Options = new() {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new TrimmingStringJsonConverter() },
  };

  private sealed record Foo(string? Bar, string[] Baz);

  [Theory]
  [InlineData("""{"bar":"qux"}""", "qux")]
  [InlineData("""{"bar":"  qux \n"}""", "qux")]
  [InlineData("""{"bar":""}""", null)]
  [InlineData("""{"bar":" \t\n"}""", null)]
  [InlineData("""{"bar":null}""", null)]
  public void Read_TrimsAndTurnsBlankIntoNull(string json, string? expected)
    => Assert.Equal(expected, JsonSerializer.Deserialize<Foo>(json, _Options)?.Bar);

  [Fact]
  public void Read_TrimsEveryStringInAnArray() {
    var foo = JsonSerializer.Deserialize<Foo>("""{"baz":[" a ","b"]}""", _Options);

    Assert.Equal(["a", "b"], foo!.Baz);
  }

  [Fact]
  public void Read_ANumberWhereAStringIsExpected_IsAJsonException()
    => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Foo>("""{"bar":5}""", _Options));

  [Fact]
  public void Write_LeavesTheTextAlone()
    => Assert.Equal("""{"bar":"  qux ","baz":[]}""", JsonSerializer.Serialize(new Foo("  qux ", []), _Options));
}
