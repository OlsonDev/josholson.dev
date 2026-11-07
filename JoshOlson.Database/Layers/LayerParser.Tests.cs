using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Layers;

public class LayerParserTests {
  private static readonly SeedSchema _Schema = new(SchemaLockSample.Create());

  [Fact]
  public void Parse_FullLayer_ReadsEverythingTyped() {
    const string Yaml = """
      description: Adds some bars
      extends: base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1000, name: baz, note: ~, created_utc: -3d 4h }
            - { bar_id: 1001, name: "null", published_utc: 2026-01-02T03:04:05Z }
          delete:
            - { bar_id: 7 }
        foo.link:
          upsert:
            - { bar_id: 1, tag_id: 2, flags: [1, 2], tags: ~ }
      """;

    var layer = LayerParser.Parse("foo", Yaml, _Schema);

    Assert.Equal("foo", layer.Name);
    Assert.Equal("base", layer.Extends);
    Assert.Equal("Adds some bars", layer.Description);
    Assert.Equal(["foo.bar", "foo.link"], layer.Tables.Select(table => table.Table));
    var bar = layer.Tables[0];
    Assert.Equal(2, bar.Upserts.Count);
    Assert.Equal(new SeedValue.Integer(1000), bar.Upserts[0]["bar_id"]);
    Assert.Equal(new SeedValue.Text("baz"), bar.Upserts[0]["name"]);
    Assert.Null(bar.Upserts[0]["note"]);
    Assert.Equal(new SeedValue.RelativeTime(-(3 * 86_400L + 4 * 3_600) * 1_000_000), bar.Upserts[0]["created_utc"]);
    Assert.Equal(new SeedValue.Text("null"), bar.Upserts[1]["name"]);
    Assert.IsType<SeedValue.AbsoluteTime>(bar.Upserts[1]["published_utc"]);
    var flags = Assert.IsType<SeedValue.Array>(layer.Tables[1].Upserts[0]["flags"]);
    Assert.Equal([new SeedValue.Integer(1), new SeedValue.Integer(2)], flags.Items);
    Assert.Equal(4, layer.RowCount);
  }

  [Fact]
  public void Parse_RootLayerWithoutTables_IsValid() {
    var layer = LayerParser.Parse("foo", "description: Nothing yet", _Schema);

    Assert.Null(layer.Extends);
    Assert.Empty(layer.Tables);
    Assert.Equal(0, layer.RowCount);
  }

  [Fact]
  public void Parse_ManyProblems_ReportsThemAllTogether() {
    const string Yaml = """
      description: Broken
      tables:
        foo.nope:
          upsert:
            - { id: 1 }
        foo.tag:
          upsert:
            - { tag_id: 99999, price: 1.234, wat: 1 }
            - { price: 1.5 }
          delete:
            - { tag_id: 1, price: 1.5 }
        foo.bar:
          upsert:
            - { bar_id: 1, name: ~, slug_length: 3 }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", Yaml, _Schema));

    Assert.Equal(
      """
      foo has 8 problems:
      - foo: foo.nope is not a table in the schema lock.
      - foo: foo.tag upsert row 1: tag_id is 99999, outside the range of smallint.
      - foo: foo.tag upsert row 1: price has 3 digits after the point, and decimal(12, 2) holds 2.
      - foo: foo.tag upsert row 1: wat is not a column of foo.tag.
      - foo: foo.tag upsert row 2: the primary key column tag_id is missing.
      - foo: foo.tag delete row 1: a delete holds only the primary key, so price does not belong.
      - foo: foo.bar upsert row 1: name is NULL, but the column is NOT NULL.
      - foo: foo.bar upsert row 1: slug_length is generated from an expression, so a seed cannot set it.
      """,
      exception.Message.Replace("\r", "", StringComparison.Ordinal));
  }

  [Fact]
  public void Parse_OneProblem_IsTheMessageAlone() {
    const string Yaml = """
      description: Broken
      tables:
        foo.tag:
          upsert:
            - { tag_id: abc }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", Yaml, _Schema));

    Assert.Equal("foo: foo.tag upsert row 1: tag_id must be a whole number, not 'abc'.", exception.Message);
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
    => Assert.Throws<SeedFormatException>(() => LayerParser.Parse(name, "description: Foo", _Schema));

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
  [InlineData("description: Foo\ntables:\n  foo.bar: { upsert: [ { bar_id: 1, bar_id: 2 } ] }")]
  [InlineData("description: [unclosed")]
  public void Parse_InvalidContent_Throws(string yaml)
    => Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", yaml, _Schema));
}
