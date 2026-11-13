using JoshOlson.Database.Layers;
using NodaTime;
using NpgsqlTypes;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class SeedSqlTests {
#pragma warning disable RCS1130 // NpgsqlDbType is a flags enum that Npgsql does not mark [Flags]: Array is OR-ed with the element type.
  private const NpgsqlDbType _SmallintArray = NpgsqlDbType.Array | NpgsqlDbType.Smallint;
#pragma warning restore RCS1130

  [Fact]
  public void Build_OneStatementPerTable_InForeignKeyOrder() {
    var layer = Base($$"""
        foo.link:
          insert:
            - {{SampleLayers.Link(1, 1)}}
        foo.tag:
          insert:
            - {{SampleLayers.Tag(1)}}
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First")}}
      """);

    var statements = Build(layer);

    Assert.Equal(["foo.bar", "foo.tag", "foo.link"], statements.Select(statement => statement.Table));
  }

  [Fact]
  public void Build_IdentityColumn_OverridesSystemValue() {
    var layer = Base($$"""
        foo.tag:
          insert:
            - {{SampleLayers.Tag(1, price: "2.5")}}
            - {{SampleLayers.Tag(2)}}
      """);

    var statement = Build(layer).Single();

    Assert.Equal(
      """
      INSERT INTO foo.tag (
          tag_id
        , price
      ) OVERRIDING SYSTEM VALUE VALUES
          (@p0, @p1)
        , (@p2, @p3)
      ;
      """,
      statement.Sql.Replace("\r", "", StringComparison.Ordinal));
    Assert.Equal(2, statement.RowCount);
    Assert.Equal(4, statement.Parameters.Count);
    Assert.Equal((short)1, statement.Parameters[0].Value);
    Assert.Equal(NpgsqlDbType.Smallint, statement.Parameters[0].NpgsqlDbType);
    Assert.Equal(2.5m, statement.Parameters[1].Value);
    Assert.Equal(NpgsqlDbType.Numeric, statement.Parameters[1].NpgsqlDbType);
  }

  [Fact]
  public void Build_ColumnGeneratedFromAnExpression_IsNotInTheStatement() {
    var layer = Base($$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First")}}
      """);

    var statement = Build(layer).Single();

    Assert.DoesNotContain("slug_length", statement.Sql, StringComparison.Ordinal);
  }

  [Fact]
  public void Build_RowThatLeavesAColumnOut_IsRefusedNotFilledByADefault() {
    var state = new SeedState();
    state.Tables["foo.tag"] = [with(RowKeyComparer.Instance)];
    state.Tables["foo.tag"][[new SeedValue.Integer(1)]] = new() { ["tag_id"] = new SeedValue.Integer(1) };

    var exception = Assert.Throws<SeedFormatException>(
      () => SeedSql.Build(state, SampleLayers.Schema, SampleLayers.Anchor));

    Assert.Equal("foo.tag: a row leaves out price, and a seed names every column.", exception.Message);
  }

  [Fact]
  public void Build_Null_IsALiteralNotAParameter() {
    var layer = Base($$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First")}}
      """);

    var statement = Build(layer).Single();

    Assert.Contains("NULL", statement.Sql, StringComparison.Ordinal);
    Assert.Equal(5, statement.Parameters.Count);
  }

  [Fact]
  public void Build_TimestampColumns_RelativeIsAnchorPlusOffset_AbsoluteIsTheInstant() {
    var layer = Base("""
        foo.bar:
          insert:
            - { bar_id: 1, slug: one, name: First, note: ~, created_utc: -1h 0.000001s, published_utc: 2020-02-03T04:05:06.000007Z, lat: 1 }
      """);

    var parameters = Build(layer).Single().Parameters;

    var created = parameters.Single(parameter => parameter.NpgsqlDbType is NpgsqlDbType.TimestampTz && parameter.Value is DateTime { Year: 2026 });
    Assert.Equal(SampleLayers.Anchor.Minus(Duration.FromHours(1)).Minus(Duration.FromNanoseconds(1000)).ToDateTimeUtc(), created.Value);
    var published = parameters.Single(parameter => parameter.Value is DateTime { Year: 2020 });
    Assert.Equal(new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc).AddTicks(70), published.Value);
  }

  [Fact]
  public void Build_Array_IsATypedArrayWithNullableItems() {
    var layer = Base($$"""
        foo.link:
          insert:
            - {{SampleLayers.Link(1, 1, flags: "[1, ~, 3]")}}
      """);

    var flags = Build(layer).Single().Parameters.Single(parameter => parameter.Value is short?[]);

    Assert.Equal(_SmallintArray, flags.NpgsqlDbType);
    Assert.Equal(new short?[] { 1, null, 3 }, flags.Value);
  }

  [Fact]
  public void Build_DomainOfAnArray_UsesTheBaseArrayType() {
    var layer = Base($$"""
        foo.link:
          insert:
            - {{SampleLayers.Link(1, 1, tags: "[7]")}}
      """);

    var tags = Build(layer).Single().Parameters.Single(parameter => parameter.Value is short?[]);

    Assert.Equal(_SmallintArray, tags.NpgsqlDbType);
  }

  [Fact]
  public void Build_TimeBeyondWhatTheAnchorCanReach_IsAProblem() {
    var layer = Base($$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First", created: "-100000000d")}}
      """);

    var exception = Assert.Throws<SeedFormatException>(() => Build(layer));

    Assert.Equal("foo.bar: created_utc holds a time outside the dates the anchor and a timestamptz can reach.", exception.Message);
  }

  [Fact]
  public void Build_NoRows_GivesNoStatements()
    => Assert.Empty(Build(LayerParser.Parse("base", "description: Nothing", SampleLayers.Schema)));

  // The root layer `base`, from the text that goes under `tables:`.
  private static SeedLayer Base(string tables)
    => SampleLayers.Layer("base", extends: null, tables);

  private static List<SeedStatement> Build(SeedLayer layer)
    => SeedSql.Build(SampleLayers.Merge(layer), SampleLayers.Schema, SampleLayers.Anchor);
}
