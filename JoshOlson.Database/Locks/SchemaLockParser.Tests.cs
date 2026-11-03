using Xunit;

namespace JoshOlson.Database.Locks;

// The writer's canonical text is what the parser must read back, and every way the parser refuses a hand edit is a
// replacement applied to that text.
public class SchemaLockParserTests {
  private static readonly string _Valid = SchemaLockWriter.Write(SchemaLockSample.Create());

  [Fact]
  public void Parse_OfWhatWasWritten_WritesTheSameText() {
    var parsed = SchemaLockParser.Parse(_Valid);

    Assert.Equal(_Valid, SchemaLockWriter.Write(parsed));
    Assert.Equal(3, parsed.Version);
    Assert.Equal([SchemaLockSample.Ccc], parsed.Migrations[1].AcceptedChecksums);
    Assert.Equal(["foo.bar", "foo.link"], parsed.Tables.Select(table => table.Name));
    Assert.Equal(TimePolicy.Absolute, parsed.Tables[0].Columns[4].Policy);
    Assert.True(parsed.Tables[0].Columns[4].Nullable);
    Assert.Equal(2, parsed.Tables[1].Columns[1].PrimaryKeyPosition);
    Assert.Equal(new ColumnType(SeedType.SmallInt, IsArray: true), parsed.Tables[1].Columns[2].Type);
  }

  [Fact]
  public void Parse_OfEmptyLock_RoundTrips() {
    var text = SchemaLockWriter.Write(new() {
      Version = 0,
      Migrations = [],
      Tables = [],
    });

    Assert.Equal(text, SchemaLockWriter.Write(SchemaLockParser.Parse(text)));
  }

  [Fact]
  public void Parse_DifferentFormatting_WritesBackTheCanonicalText() {
    var reformatted = $$"""
      version: 3
      migrations:
        - { version: 1, file: V001__foo.sql, checksum: {{SchemaLockSample.Aaa}} }
        - version: 3
          file: V003__bar.sql
          checksum: {{SchemaLockSample.Bbb}}
          accepted_checksums: [{{SchemaLockSample.Ccc}}]
      tables:
        foo.bar:
          - name: bar_id
            type: bigint
            primary_key: 1
          - { name: name, type: text }
          - { type: text, name: note, nullable: true }
          - { name: created_utc, type: timestamptz, policy: relative }
          - { name: published_utc, type: timestamptz, nullable: true, policy: absolute }
        foo.link:
          - { name: bar_id, type: bigint, primary_key: 1 }
          - { name: tag_id, type: smallint, primary_key: 2 }
          - { name: flags, type: "smallint[]", nullable: true }
      """;

    Assert.Equal(_Valid, SchemaLockWriter.Write(SchemaLockParser.Parse(reformatted)));
  }

  [Fact]
  public void RoundTrip_NamesThatNeedQuoting() {
    var schemaLock = SchemaLockSample.Create() with {
      Tables = [
        new() {
          Name = "foo.bar",
          Columns = [
            SchemaLockSample.Column("my col: \"x\"", "text"),
            SchemaLockSample.Column("-dash", "text"),
            SchemaLockSample.Column("null", "text"),
          ],
        },
      ],
    };

    var text = SchemaLockWriter.Write(schemaLock);
    var parsed = SchemaLockParser.Parse(text);

    Assert.Equal(["my col: \"x\"", "-dash", "null"], parsed.Tables[0].Columns.Select(column => column.Name));
    Assert.Equal(text, SchemaLockWriter.Write(parsed));
  }

  [Theory]
  [InlineData("version: 3\nmigrations:", "version: -1\nmigrations:")]
  [InlineData("version: 3\nmigrations:", "version: three\nmigrations:")]
  [InlineData("version: 3\nmigrations:", "migrations:")]
  [InlineData("version: 3\n", "version: 3\nextra: 1\n")]
  [InlineData("migrations:\n", "migrations: nope\n")]
  [InlineData("tables:\n", "tablez:\n")]
  [InlineData("file: V001__foo.sql", "file: V002__foo.sql")]
  [InlineData("file: V001__foo.sql", "file: foo.sql")]
  [InlineData("  - version: 3\n    file: V003__bar.sql", "  - version: 4\n    file: V004__bar.sql")]
  [InlineData("  - version: 3\n    file: V003__bar.sql", "  - version: 1\n    file: V001__bar.sql")]
  [InlineData("    checksum: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "    checksum: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
  [InlineData("    checksum: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "    checksum: abc")]
  [InlineData("      - cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", "      - nope")]
  [InlineData("foo.link:", "foolink:")]
  [InlineData("foo.link:", "foo.bar:")]
  [InlineData("name: name, type: text", "name: name, type: interval")]
  [InlineData("nullable: true, policy", "nullable: maybe, policy")]
  [InlineData("tag_id, type: smallint, primary_key: 2", "tag_id, type: smallint, primary_key: 3")]
  [InlineData("tag_id, type: smallint, primary_key: 2", "tag_id, type: smallint, primary_key: 1")]
  [InlineData("tag_id, type: smallint, primary_key: 2", "tag_id, type: smallint, primary_key: 0")]
  [InlineData("type: timestamptz, policy: relative", "type: timestamptz")]
  [InlineData("name: name, type: text", "name: name, type: text, policy: relative")]
  [InlineData("policy: relative", "policy: sometimes")]
  [InlineData("{ name: note,", "{ name: name,")]
  [InlineData("{ name: name,", "{ name: name, color: red,")]
  public void Parse_HandEditedOrMalformed_Throws(string find, string replace) {
    Assert.Contains(find, _Valid, StringComparison.Ordinal);

    Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(_Valid.Replace(find, replace, StringComparison.Ordinal)));
  }

  [Theory]
  [InlineData("")]
  [InlineData("- version: 1")]
  [InlineData("version: [unclosed")]
  [InlineData("just text")]
  public void Parse_NotALockFile_Throws(string yaml)
    => Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(yaml));

  [Fact]
  public void Parse_Error_NamesTheFile() {
    var exception = Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse("version: x\nmigrations: []\ntables: {}"));

    Assert.StartsWith("schema.lock.yaml:", exception.Message, StringComparison.Ordinal);
  }
}
