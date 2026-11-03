using Xunit;

namespace JoshOlson.Database.Layers;

public class LayerParserTests {
  [Fact]
  public void Parse_FullLayer_ReadsEverything() {
    var layer = LayerParser.Parse("foo", """
      description: Adds some bars
      extends: base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1000, name: baz, qux: ~ }
            - { bar_id: 1001, name: "null" }
          delete:
            - { bar_id: 7 }
        foo.corge:
          upsert:
            - { corge_id: 1 }
      """);

    Assert.Equal("foo", layer.Name);
    Assert.Equal("base", layer.Extends);
    Assert.Equal("Adds some bars", layer.Description);
    Assert.Equal(["foo.bar", "foo.corge"], layer.Tables.Select(table => table.Table));
    var bar = layer.Tables[0];
    Assert.Equal(2, bar.Upserts.Count);
    Assert.Equal("1000", bar.Upserts[0]["bar_id"]);
    Assert.Equal("baz", bar.Upserts[0]["name"]);
    Assert.Equal(4, layer.RowCount);
  }

  [Fact]
  public void Parse_NullForms_BecomeNullOnlyWhenPlain() {
    var layer = LayerParser.Parse("foo", """
      description: Nulls
      tables:
        foo.bar:
          upsert:
            - { a: ~, b: null, c: , d: "null", e: '~' }
      """);

    var row = layer.Tables[0].Upserts[0];
    Assert.Null(row["a"]);
    Assert.Null(row["b"]);
    Assert.Null(row["c"]);
    Assert.Equal("null", row["d"]);
    Assert.Equal("~", row["e"]);
  }

  [Fact]
  public void Parse_RootLayerWithoutTables_IsValid() {
    var layer = LayerParser.Parse("foo", "description: Nothing yet");

    Assert.Null(layer.Extends);
    Assert.Empty(layer.Tables);
    Assert.Equal(0, layer.RowCount);
  }

  [Theory]
  [InlineData("")]
  [InlineData("Foo")]
  [InlineData("foo_bar")]
  [InlineData("foo--bar")]
  [InlineData("-foo")]
  [InlineData("foo-")]
  [InlineData("1foo")]
  [InlineData("foo bar")]
  public void Parse_InvalidName_Throws(string name)
    => Assert.Throws<SeedFormatException>(() => LayerParser.Parse(name, "description: Foo"));

  [Theory]
  [InlineData("")]
  [InlineData("- foo")]
  [InlineData("description: ")]
  [InlineData("extends: base")]
  [InlineData("description: Foo\nbar: baz")]
  [InlineData("description: Foo\nextends: Not Valid")]
  [InlineData("description: Foo\ntables: [foo]")]
  [InlineData("description: Foo\ntables:\n  bar: {}")]
  [InlineData("description: Foo\ntables:\n  foo.bar: { merge: [] }")]
  [InlineData("description: Foo\ntables:\n  foo.bar: { upsert: { a: 1 } }")]
  [InlineData("description: Foo\ntables:\n  foo.bar: { upsert: [ {} ] }")]
  [InlineData("description: Foo\ntables:\n  foo.bar: { upsert: [ { a: [1] } ] }")]
  [InlineData("description: Foo\ntables:\n  foo.bar: { upsert: [ { a: 1, a: 2 } ] }")]
  [InlineData("description: [unclosed")]
  public void Parse_InvalidContent_Throws(string yaml)
    => Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", yaml));
}
