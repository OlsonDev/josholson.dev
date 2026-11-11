using JoshOlson.Database.Locks;
using JoshOlson.Database.Schema;
using NodaTime;
using Xunit;

namespace JoshOlson.Database.Layers;

public class LayerWriterTests {
  private static readonly SeedSchema _Schema = Schema();

  // The sample lock, and a table with one column of each type it has no column for.
  private static SeedSchema Schema() {
    var sample = SchemaLockSample.Create();
    var kind = SchemaLockSample.NewTable("foo.kind") with {
      Columns = [
        SchemaLockSample.Column("id", "bigint"),
        SchemaLockSample.Column("flag", "boolean", nullable: true),
        SchemaLockSample.Column("blob", "bytea", nullable: true),
        SchemaLockSample.Column("doc", "jsonb", nullable: true),
        SchemaLockSample.Column("address", "inet", nullable: true),
        SchemaLockSample.Column("note", "text", nullable: true),
        SchemaLockSample.Column("amount", "decimal(9, 2)", nullable: true),
        SchemaLockSample.Column("at_utc", "timestamptz", nullable: true, policy: TimePolicy.Relative),
        SchemaLockSample.Column("seen_utc", "timestamptz", nullable: true, policy: TimePolicy.Absolute),
        SchemaLockSample.Column("nums", "integer[]", nullable: true),
        SchemaLockSample.Column("docs", "jsonb[]", nullable: true),
      ],
    };
    return new(sample with { Tables = [.. sample.Tables, kind] });
  }

  private static Dictionary<string, SeedValue?> Row(params (string Column, SeedValue? Value)[] cells)
    => cells.ToDictionary(cell => cell.Column, cell => cell.Value, StringComparer.Ordinal);

  private static SeedLayer Layer(params TableChange[] tables)
    => new() {
      Name = "foo",
      Extends = null,
      Description = "Foo",
      Tables = [.. tables],
    };

  private static TableChange Upserts(string table, params Dictionary<string, SeedValue?>[] rows)
    => new() {
      Table = table,
      Upserts = [.. rows],
      Deletes = [],
    };

  [Theory]
  [InlineData("minimal")]
  [InlineData("more-tags")]
  public async Task Write_TheRealLayers_GiveBackTheirFilesByteForByte(string name) {
    var schemaLock = SchemaLockParser.Parse(await File.ReadAllTextAsync(SeedPaths.ForRepository().LockFile, TestContext.Current.CancellationToken));
    var schema = new SeedSchema(schemaLock);
    var yaml = await File.ReadAllTextAsync(Path.Combine(SeedPaths.ForRepository().Seeds, name + ".yaml"), TestContext.Current.CancellationToken);

    var written = LayerWriter.Write(LayerParser.Parse(name, yaml, schema), schema);

    Assert.Equal(yaml, written);
  }

  [Fact]
  public void Write_EveryKindOfValue_IsWrittenAsTheReaderReadsIt() {
    var layer = Layer(Upserts(
      "foo.kind",
      Row(
        ("id", new SeedValue.Integer(1)),
        ("flag", new SeedValue.Boolean(true)),
        ("blob", new SeedValue.Bytes("00ff")),
        ("doc", new SeedValue.Json("""{"a":[1,2.50,{"b":null}],"c":"true","d":"x: y"}""")),
        ("address", new SeedValue.Inet("10.0.0.0/8")),
        ("note", new SeedValue.Text("hello")),
        ("amount", new SeedValue.Decimal(1.50m)),
        ("at_utc", new SeedValue.RelativeTime(-(3 * 86_400L + 4 * 3_600) * 1_000_000)),
        ("seen_utc", new SeedValue.AbsoluteTime(Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(123_456_000))),
        ("nums", new SeedValue.Array([new SeedValue.Integer(1), null, new SeedValue.Integer(3)])),
        ("docs", new SeedValue.Array([new SeedValue.Json("""{"k":"v"}"""), null])))));

    var yaml = LayerWriter.Write(layer, _Schema);

    Assert.Equal(
      """
      description: Foo
      tables:
        foo.kind:
          upsert:
            - { id: 1, flag: true, blob: \x00ff, doc: { a: [1, 2.50, { b: null }], c: "true", d: "x: y" }, address: 10.0.0.0/8, note: hello, amount: 1.50, at_utc: -3d 4h, seen_utc: 2026-01-02T03:04:05.123456Z, nums: [1, ~, 3], docs: [{ k: "v" }, ~] }

      """,
      yaml);
    var parsed = LayerParser.Parse("foo", yaml, _Schema);
    Assert.Equal(yaml, LayerWriter.Write(parsed, _Schema));
    var row = parsed.Tables[0].Upserts[0];
    Assert.Equal(new SeedValue.Json("""{"a":[1,2.50,{"b":null}],"c":"true","d":"x: y"}"""), row["doc"]);
    Assert.Equal(new SeedValue.Decimal(1.50m), row["amount"]);
    Assert.Equal(new SeedValue.Inet("10.0.0.0/8"), row["address"]);
  }

  [Theory]
  [InlineData("foo")]
  [InlineData("null")]
  [InlineData("~")]
  [InlineData("")]
  [InlineData(" leading")]
  [InlineData("trailing ")]
  [InlineData("   ")]
  [InlineData("a: b")]
  [InlineData("a:b")]
  [InlineData("ends with:")]
  [InlineData("a #b")]
  [InlineData("a#b")]
  [InlineData("#hash")]
  [InlineData("- x")]
  [InlineData("-x")]
  [InlineData("-")]
  [InlineData("{a}")]
  [InlineData("a, b")]
  [InlineData("[x]")]
  [InlineData("? x")]
  [InlineData("*star")]
  [InlineData("&anchor")]
  [InlineData("!tag")]
  [InlineData("%percent")]
  [InlineData("@at")]
  [InlineData("`tick")]
  [InlineData("|pipe")]
  [InlineData(">fold")]
  [InlineData("it's")]
  [InlineData("say \"hi\"")]
  [InlineData("back\\slash")]
  [InlineData("tab\there")]
  [InlineData("line\nbreak")]
  [InlineData("carriage\rreturn")]
  [InlineData("bell\u0007")]
  [InlineData("日本語 ✓")]
  [InlineData("100")]
  [InlineData("true")]
  [InlineData("1e5")]
  [InlineData("0x1F")]
  [InlineData("2026-01-02T03:04:05Z")]
  public void Write_AnyText_IsReadBackAsTheSameText(string text) {
    var layer = Layer(Upserts("foo.kind", Row(("id", new SeedValue.Integer(1)), ("note", new SeedValue.Text(text)))));

    var yaml = LayerWriter.Write(layer, _Schema);

    var parsed = LayerParser.Parse("foo", yaml, _Schema);
    Assert.Equal(new SeedValue.Text(text), parsed.Tables[0].Upserts[0]["note"]);
    Assert.Equal(yaml, LayerWriter.Write(parsed, _Schema));
  }

  [Theory]
  [InlineData("Foo: bar")]
  [InlineData("Adds the rest, and more")]
  [InlineData("null")]
  [InlineData("- not a list")]
  [InlineData("it's # not a comment")]
  public void Write_AnyDescription_IsReadBackAsTheSameDescription(string description) {
    var layer = Layer() with { Description = description };

    var parsed = LayerParser.Parse("foo", LayerWriter.Write(layer, _Schema), _Schema);

    Assert.Equal(description, parsed.Description);
  }

  [Fact]
  public void Write_NullValues_AreATildeAndSurviveTheRoundTrip() {
    var layer = Layer(Upserts("foo.kind", Row(("id", new SeedValue.Integer(1)), ("note", null), ("nums", null))));

    var yaml = LayerWriter.Write(layer, _Schema);

    Assert.Contains("{ id: 1, note: ~, nums: ~ }", yaml, StringComparison.Ordinal);
    Assert.Null(LayerParser.Parse("foo", yaml, _Schema).Tables[0].Upserts[0]["note"]);
  }

  [Fact]
  public void Write_ScrambledInput_IsCanonical() {
    var layer = new SeedLayer {
      Name = "foo",
      Extends = "base",
      Description = "Foo",
      Tables = [
        new() {
          Table = "foo.tag",
          Upserts = [Row(("tag_id", new SeedValue.Integer(5)), ("price", new SeedValue.Decimal(2.00m)))],
          Deletes = [],
        },
        new() {
          Table = "foo.kind",
          Upserts = [
            Row(("note", new SeedValue.Text("b")), ("id", new SeedValue.Integer(10))),
            Row(("note", new SeedValue.Text("a")), ("id", new SeedValue.Integer(2))),
          ],
          Deletes = [Row(("id", new SeedValue.Integer(9))), Row(("id", new SeedValue.Integer(3)))],
        },
        new() {
          Table = "foo.link",
          Upserts = [],
          Deletes = [],
        },
      ],
    };

    var yaml = LayerWriter.Write(layer, _Schema);

    Assert.Equal(
      """
      description: Foo
      extends: base
      tables:
        foo.kind:
          upsert:
            - { id: 2, note: a }
            - { id: 10, note: b }
          delete:
            - { id: 3 }
            - { id: 9 }
        foo.tag:
          upsert:
            - { tag_id: 5, price: 2.00 }

      """,
      yaml);
  }

  [Fact]
  public void Write_CompositeKey_SortsByEachKeyColumnInOrder() {
    var layer = Layer(Upserts(
      "foo.link",
      Row(("bar_id", new SeedValue.Integer(2)), ("tag_id", new SeedValue.Integer(1))),
      Row(("bar_id", new SeedValue.Integer(1)), ("tag_id", new SeedValue.Integer(9))),
      Row(("bar_id", new SeedValue.Integer(1)), ("tag_id", new SeedValue.Integer(2)))));

    var yaml = LayerWriter.Write(layer, _Schema);

    Assert.Equal(
      ["{ bar_id: 1, tag_id: 2 }", "{ bar_id: 1, tag_id: 9 }", "{ bar_id: 2, tag_id: 1 }"],
      yaml.Split('\n').Where(line => line.Contains("bar_id", StringComparison.Ordinal)).Select(line => line.Trim()[2..]));
  }

  [Fact]
  public void Write_LayerWithNoTables_IsJustTheDescription()
    => Assert.Equal("description: Foo\n", LayerWriter.Write(Layer(), _Schema));

  [Fact]
  public void Write_UnknownTable_Throws()
    => _ = Assert.Throws<SeedFormatException>(
      () => LayerWriter.Write(Layer(Upserts("foo.nope", Row(("id", new SeedValue.Integer(1))))), _Schema));

  [Fact]
  public void Write_UnknownColumn_Throws()
    => _ = Assert.Throws<SeedFormatException>(
      () => LayerWriter.Write(Layer(Upserts("foo.kind", Row(("id", new SeedValue.Integer(1)), ("nope", null)))), _Schema));
}
