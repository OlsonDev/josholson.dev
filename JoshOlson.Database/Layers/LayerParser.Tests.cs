using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Layers;

public class LayerParserTests {
  [Fact]
  public void Parse_FullLayer_ReadsEverythingTyped() {
    const string Yaml = """
      description: Adds some bars
      extends: base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1001, slug: baz, name: baz, note: ~, created_utc: -3d 4h, published_utc: ~, lat: 1.5 }
            - { bar_id: 1002, slug: qux, name: "null", note: ~, created_utc: 0s, published_utc: 2026-01-02T03:04:05Z, lat: 0 }
          update:
            - { bar_id: 1, name: quux }
          delete:
            - { bar_id: 7 }
        foo.link:
          insert:
            - { bar_id: 1, tag_id: 2, flags: [1, 2], tags: ~ }
      """;

    var layer = LayerParser.Parse("foo", Yaml, SampleLayers.Schema);

    Assert.Equal("foo", layer.Name);
    Assert.Equal("base", layer.Extends);
    Assert.Equal("Adds some bars", layer.Description);
    Assert.Equal(["foo.bar", "foo.link"], layer.Tables.Select(table => table.Table));
    var bar = layer.Tables[0];
    Assert.Equal(2, bar.Inserts.Count);
    Assert.Equal(new SeedValue.Integer(1001), bar.Inserts[0]["bar_id"]);
    Assert.Equal(new SeedValue.Text("baz"), bar.Inserts[0]["name"]);
    Assert.Null(bar.Inserts[0]["note"]);
    Assert.Equal(new SeedValue.RelativeTime(-(3 * 86_400L + 4 * 3_600) * 1_000_000), bar.Inserts[0]["created_utc"]);
    Assert.Equal(new SeedValue.Text("null"), bar.Inserts[1]["name"]);
    Assert.IsType<SeedValue.AbsoluteTime>(bar.Inserts[1]["published_utc"]);
    Assert.Equal(new SeedValue.Text("quux"), Assert.Single(bar.Updates)["name"]);
    Assert.Equal(new SeedValue.Integer(7), Assert.Single(bar.Deletes)["bar_id"]);
    var flags = Assert.IsType<SeedValue.Array>(layer.Tables[1].Inserts[0]["flags"]);
    Assert.Equal([new SeedValue.Integer(1), new SeedValue.Integer(2)], flags.Items);
    Assert.Equal(5, layer.RowCount);
  }

  [Fact]
  public void Parse_RootLayerWithoutTables_IsValid() {
    var layer = LayerParser.Parse("foo", "description: Nothing yet", SampleLayers.Schema);

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
          insert:
            - { id: 1 }
        foo.tag:
          insert:
            - { tag_id: 99999, price: 1.234, wat: 1 }
            - { price: 1.5 }
          update:
            - { tag_id: 1 }
          delete:
            - { tag_id: 1, price: 1.5 }
        foo.bar:
          insert:
            - { bar_id: 1, name: ~, slug_length: 3 }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", Yaml, SampleLayers.Schema));

    Assert.Equal(
      """
      foo has 11 problems:
      - foo: foo.nope is not a table in the schema lock.
      - foo: foo.tag insert row 1: tag_id is 99999, outside the range of smallint.
      - foo: foo.tag insert row 1: price has 3 digits after the point, and decimal(12, 2) holds 2.
      - foo: foo.tag insert row 1: wat is not a column of foo.tag.
      - foo: foo.tag insert row 2: the primary key column tag_id is missing.
      - foo: foo.tag insert row 2: an insert names every column, and it leaves out tag_id.
      - foo: foo.tag update row 1: an update names the key and the columns it changes, and it changes none.
      - foo: foo.tag delete row 1: a delete holds only the primary key, so price does not belong.
      - foo: foo.bar insert row 1: name is NULL, but the column is NOT NULL.
      - foo: foo.bar insert row 1: slug_length is generated from an expression, so a seed cannot set it.
      - foo: foo.bar insert row 1: an insert names every column, and it leaves out slug, note, created_utc, published_utc, lat.
      """,
      exception.Message.Replace("\r", "", StringComparison.Ordinal));
  }

  [Fact]
  public void Parse_OneProblem_IsTheMessageAlone() {
    const string Yaml = """
      description: Broken
      tables:
        foo.tag:
          update:
            - { tag_id: 1, price: abc }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", Yaml, SampleLayers.Schema));

    Assert.Equal("foo: foo.tag update row 1: price must be a number, not 'abc'.", exception.Message);
  }

  [Fact]
  public void Parse_InsertThatLeavesColumnsOut_NamesThem() {
    const string Yaml = """
      description: Broken
      tables:
        foo.link:
          insert:
            - { bar_id: 1, tag_id: 2 }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", Yaml, SampleLayers.Schema));

    Assert.Equal("foo: foo.link insert row 1: an insert names every column, and it leaves out flags, tags.", exception.Message);
  }

  [Fact]
  public void Parse_InsertNamingANullableColumnAsNull_IsComplete() {
    const string Yaml = """
      description: Fine
      tables:
        foo.link:
          insert:
            - { bar_id: 1, tag_id: 2, flags: ~, tags: ~ }
      """;

    var layer = LayerParser.Parse("foo", Yaml, SampleLayers.Schema);

    Assert.Null(layer.Tables[0].Inserts[0]["flags"]);
    Assert.True(layer.Tables[0].Inserts[0].ContainsKey("flags"));
  }

  [Fact]
  public void Parse_UpdateNamingOnlyTheKey_ChangesNothingAndIsRefused() {
    const string Yaml = """
      description: Broken
      tables:
        foo.bar:
          update:
            - { bar_id: 1 }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", Yaml, SampleLayers.Schema));

    Assert.Equal("foo: foo.bar update row 1: an update names the key and the columns it changes, and it changes none.", exception.Message);
  }

  [Fact]
  public void Parse_UpdateNamingSomeColumns_IsFine() {
    const string Yaml = """
      description: Fine
      tables:
        foo.bar:
          update:
            - { bar_id: 1, note: changed }
      """;

    var row = Assert.Single(LayerParser.Parse("foo", Yaml, SampleLayers.Schema).Tables[0].Updates);

    Assert.Equal(["bar_id", "note"], row.Keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public void Parse_TableThatIsIgnored_CannotBeNamed() {
    var schema = SampleLayers.SchemaWith(SchemaLockSample.NewTable("foo.session") with { Ignored = true });
    const string Yaml = """
      description: Broken
      tables:
        foo.session:
          insert:
            - { id: 1 }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", Yaml, schema));

    Assert.Equal(
      "foo: foo.session holds runtime data (its comment says @seed ignore), not seed data, so a layer cannot name it.",
      exception.Message);
  }

  [Fact]
  public void Parse_Upsert_IsNoLongerASection()
    => Assert.Throws<SeedFormatException>(
      () => LayerParser.Parse(
        "foo",
        "description: Foo\ntables:\n  foo.link: { upsert: [ { bar_id: 1, tag_id: 2 } ] }",
        SampleLayers.Schema));

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
    => Assert.Throws<SeedFormatException>(() => LayerParser.Parse(name, "description: Foo", SampleLayers.Schema));

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
  [InlineData("description: Foo\ntables:\n  foo.bar: { insert: { a: 1 } }")]
  [InlineData("description: Foo\ntables:\n  foo.bar: { insert: [ {} ] }")]
  [InlineData("description: Foo\ntables:\n  foo.bar: { insert: [ { bar_id: 1, bar_id: 2 } ] }")]
  [InlineData("description: [unclosed")]
  public void Parse_InvalidContent_Throws(string yaml)
    => Assert.Throws<SeedFormatException>(() => LayerParser.Parse("foo", yaml, SampleLayers.Schema));
}
