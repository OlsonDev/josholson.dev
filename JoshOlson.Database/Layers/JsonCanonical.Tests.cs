using Xunit;

namespace JoshOlson.Database.Layers;

public class JsonCanonicalTests {
  [Theory]
  [InlineData("""{ "foo": 1, "bar": 2 }""", """{"bar":2,"foo":1}""")]
  [InlineData("""{ "zz": 1, "aaa": 2, "b": 3 }""", """{"b":3,"zz":1,"aaa":2}""")]
  [InlineData("""{ "a": 1, "a": 2 }""", """{"a":2}""")]
  [InlineData("""{ "b": { "d": 1, "c": [ { "y": 1, "x": 2 } ] } }""", """{"b":{"c":[{"x":2,"y":1}],"d":1}}""")]
  [InlineData("""[ 3, 1, { "b": 1, "a": 2 } ]""", """[3,1,{"a":2,"b":1}]""")]
  [InlineData("""{ "n": 2.50, "m": -0.0010, "big": 12345678901234567890.5 }""", """{"m":-0.0010,"n":2.50,"big":12345678901234567890.5}""")]
  [InlineData("""{ "t": true, "f": false, "z": null, "s": "x: y" }""", """{"f":false,"s":"x: y","t":true,"z":null}""")]
  [InlineData("{}", "{}")]
  [InlineData("[]", "[]")]
  [InlineData("\"text\"", "\"text\"")]
  [InlineData("42", "42")]
  [InlineData("null", "null")]
  public void Normalize_Json_IsCompactAndInPostgresKeyOrder(string json, string expected)
    => Assert.Equal(expected, JsonCanonical.Normalize(json));

  // "é" is two bytes and "zz" is two bytes too, so they tie on length and compare as bytes (0xC3 is above 'z'); "a" is one.
  [Fact]
  public void Normalize_KeysOfDifferentByteLengths_SortByBytesNotCharacters()
    => Assert.Equal("""{"a":1,"zz":2,"é":3}""", JsonCanonical.Normalize("""{ "é": 3, "zz": 2, "a": 1 }"""));

  [Fact]
  public void Normalize_AlreadyNormal_IsUnchanged() {
    const string Json = """{"b":1,"aa":{"c":[1,2.50,null]}}""";

    Assert.Equal(Json, JsonCanonical.Normalize(Json));
  }
}
