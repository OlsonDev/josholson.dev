using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using NodaTime;
using NpgsqlTypes;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class SeedSqlTests {
  private static readonly SeedSchema _Schema = new(SchemaLockSample.Create());
  private static readonly Instant _Anchor = Instant.FromUtc(2026, 1, 1, 12, 0);
#pragma warning disable RCS1130 // NpgsqlDbType is a flags enum that Npgsql does not mark [Flags]: Array is OR-ed with the element type.
  private const NpgsqlDbType _SmallintArray = NpgsqlDbType.Array | NpgsqlDbType.Smallint;
#pragma warning restore RCS1130

  [Fact]
  public void Build_OneStatementPerTable_InForeignKeyOrder() {
    const string Yaml = """
      description: Base
      tables:
        foo.link:
          upsert:
            - { bar_id: 1, tag_id: 1 }
        foo.tag:
          upsert:
            - { tag_id: 1 }
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1 }
      """;

    var statements = Build(Yaml);

    Assert.Equal(["foo.bar", "foo.tag", "foo.link"], statements.Select(statement => statement.Table));
  }

  [Fact]
  public void Build_IdentityColumn_OverridesSystemValue_AndMissingColumnsAreDefault() {
    const string Yaml = """
      description: Base
      tables:
        foo.tag:
          upsert:
            - { tag_id: 1, price: 2.5 }
            - { tag_id: 2 }
      """;

    var statement = Build(Yaml).Single();

    Assert.Equal(
      """
      INSERT INTO foo.tag (
          tag_id
        , price
      ) OVERRIDING SYSTEM VALUE VALUES
          (@p0, @p1)
        , (@p2, DEFAULT)
      ;
      """,
      statement.Sql.Replace("\r", "", StringComparison.Ordinal));
    Assert.Equal(2, statement.RowCount);
    Assert.Equal(3, statement.Parameters.Count);
    Assert.Equal((short)1, statement.Parameters[0].Value);
    Assert.Equal(NpgsqlDbType.Smallint, statement.Parameters[0].NpgsqlDbType);
    Assert.Equal(2.5m, statement.Parameters[1].Value);
    Assert.Equal(NpgsqlDbType.Numeric, statement.Parameters[1].NpgsqlDbType);
  }

  [Fact]
  public void Build_Null_IsALiteralNotAParameter() {
    const string Yaml = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1, note: ~ }
      """;

    var statement = Build(Yaml).Single();

    Assert.Contains("NULL", statement.Sql, StringComparison.Ordinal);
    Assert.DoesNotContain(statement.Parameters, parameter => parameter.ParameterName is "p4" or "p5");
  }

  [Fact]
  public void Build_TimestampColumns_RelativeIsAnchorPlusOffset_AbsoluteIsTheInstant() {
    const string Yaml = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1, created_utc: -1h 0.000001s, published_utc: 2020-02-03T04:05:06.000007Z }
      """;

    var parameters = Build(Yaml).Single().Parameters;

    var created = parameters.Single(parameter => parameter.NpgsqlDbType is NpgsqlDbType.TimestampTz && parameter.Value is DateTime { Year: 2026 });
    Assert.Equal(_Anchor.Minus(Duration.FromHours(1)).Minus(Duration.FromNanoseconds(1000)).ToDateTimeUtc(), created.Value);
    var published = parameters.Single(parameter => parameter.Value is DateTime { Year: 2020 });
    Assert.Equal(new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc).AddTicks(70), published.Value);
  }

  [Fact]
  public void Build_Array_IsATypedArrayWithNullableItems() {
    const string Yaml = """
      description: Base
      tables:
        foo.link:
          upsert:
            - { bar_id: 1, tag_id: 1, flags: [1, ~, 3] }
      """;

    var flags = Build(Yaml).Single().Parameters.Single(parameter => parameter.Value is short?[]);

    Assert.Equal(_SmallintArray, flags.NpgsqlDbType);
    Assert.Equal(new short?[] { 1, null, 3 }, flags.Value);
  }

  [Fact]
  public void Build_DomainOfAnArray_UsesTheBaseArrayType() {
    const string Yaml = """
      description: Base
      tables:
        foo.link:
          upsert:
            - { bar_id: 1, tag_id: 1, tags: [7] }
      """;

    var tags = Build(Yaml).Single().Parameters.Single(parameter => parameter.Value is short?[]);

    Assert.Equal(_SmallintArray, tags.NpgsqlDbType);
  }

  [Fact]
  public void Build_TimeBeyondWhatTheAnchorCanReach_IsAProblem() {
    const string Yaml = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1, created_utc: -100000000d }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => Build(Yaml));

    Assert.Equal("foo.bar: created_utc holds a time outside the dates the anchor and a timestamptz can reach.", exception.Message);
  }

  [Fact]
  public void Build_NoRows_GivesNoStatements()
    => Assert.Empty(Build("description: Nothing"));

  private static List<SeedStatement> Build(string yaml) {
    var layer = LayerParser.Parse("base", yaml, _Schema);
    return SeedSql.Build(LayerMerger.Merge([layer], _Schema), _Schema, _Anchor);
  }
}
