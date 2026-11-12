using JoshOlson.Database.Schema;
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
    Assert.Equal(["foo.bar", "foo.link", "foo.tag"], parsed.Tables.Select(table => table.Name));
  }

  [Fact]
  public void Parse_LayerChecksums_RoundTripAndAnAbsentSectionIsNull() {
    var withLayers = SchemaLockWriter.Write(SchemaLockSample.Create() with {
      Layers = [new("bar", SchemaLockSample.Aaa), new("foo-baz", SchemaLockSample.Bbb)],
    });
    var empty = SchemaLockWriter.Write(SchemaLockSample.Create() with { Layers = [] });

    var parsed = SchemaLockParser.Parse(withLayers);

    Assert.Equal(withLayers, SchemaLockWriter.Write(parsed));
    Assert.Equal([new("bar", SchemaLockSample.Aaa), new("foo-baz", SchemaLockSample.Bbb)], parsed.Layers);
    Assert.Equal([], SchemaLockParser.Parse(empty).Layers);
    Assert.Null(SchemaLockParser.Parse(_Valid).Layers);
  }

  // The layers section of a lock with one layer, `bar`, replaced by each of these (`{A}` is a valid checksum).
  [Theory]
  [InlineData("  Bar: {A}\n", "not a layer name")]
  [InlineData("  bar: abc\n", "checksum")]
  [InlineData("  foo: {A}\n  bar: {A}\n", "out of order")]
  [InlineData("  bar: [{A}]\n", "bar")]
  public void Parse_BadLayersSection_Throws(string section, string expected) {
    var text = SchemaLockWriter.Write(SchemaLockSample.Create() with {
      Layers = [new("bar", SchemaLockSample.Aaa)],
    });
    var edited = text.Replace(
      $"  bar: {SchemaLockSample.Aaa}\n",
      section.Replace("{A}", SchemaLockSample.Aaa, StringComparison.Ordinal),
      StringComparison.Ordinal);

    var exception = Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(edited));

    Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Parse_ReadsEveryColumnProperty() {
    var parsed = SchemaLockParser.Parse(_Valid);

    var bar = parsed.Tables[0].Columns;
    Assert.Equal(GeneratedAs.IdentityAlways, bar[0].GeneratedAs);
    Assert.Equal("foo.short_slug", bar[1].Type);
    Assert.True(bar[3].Nullable);
    Assert.True(bar[3].DeclaredNullable);
    Assert.False(bar[6].Nullable);
    Assert.True(bar[6].DeclaredNullable);
    Assert.Equal(TimePolicy.Relative, bar[4].Policy);
    Assert.Equal(TimePolicy.Absolute, bar[5].Policy);
    Assert.Equal(GeneratedAs.ExpressionStored, bar[7].GeneratedAs);
    var link = parsed.Tables[1].Columns;
    Assert.Equal("smallint[]", link[2].Type);
    Assert.Null(link[2].GeneratedAs);
    Assert.Equal("decimal(12, 2)", parsed.Tables[2].Columns[1].Type);
  }

  [Fact]
  public void Parse_ReadsDomainsWithTheirTypesAndChecks() {
    var domains = SchemaLockParser.Parse(_Valid).Domains;

    Assert.Equal(["foo.slug", "foo.short_slug", "foo.coords", "foo.tag_ids"], domains.Select(domain => domain.Name));
    Assert.Equal("foo.slug", domains[1].Type);
    Assert.Equal("decimal(9, 6)", domains[2].Type);
    Assert.Equal("smallint[]", domains[3].Type);
    var check = Assert.Single(domains[0].Checks);
    Assert.Equal("slug_format", check.Name);
    Assert.Equal("CHECK ((VALUE ~ '^[a-z0-9-]+$'::text))", check.Expression);
    Assert.Empty(domains[2].Checks);
  }

  [Fact]
  public void Parse_NoDomainsKey_MeansNoDomains() {
    var text = SchemaLockWriter.Write(SchemaLockSample.Create() with {
      Domains = [],
      Tables = [SchemaLockSample.Table("foo.bar")],
    });

    Assert.DoesNotContain("domains", text, StringComparison.Ordinal);
    Assert.Empty(SchemaLockParser.Parse(text).Domains);
  }

  [Fact]
  public void Parse_TwoChecksWithTheSameName_Throws() {
    var text = _Valid.Replace(
      "      - { name: slug_format, expression: \"CHECK ((VALUE ~ '^[a-z0-9-]+$'::text))\" }",
      "      - { name: slug_format, expression: \"CHECK ((VALUE ~ '^[a-z0-9-]+$'::text))\" }\n      - { name: slug_format, expression: \"CHECK ((VALUE != ''::text))\" }",
      StringComparison.Ordinal);

    var exception = Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(text));

    Assert.Contains("two checks named slug_format", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Parse_ColumnWithAnUnknownDomain_NamesTheColumn() {
    var exception = Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(_Valid.Replace("type: foo.short_slug,", "type: foo.nope,", StringComparison.Ordinal)));

    Assert.Contains("foo.bar.slug", exception.Message, StringComparison.Ordinal);
    Assert.Contains("foo.nope", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Parse_PolicyOnAColumnWhoseDomainIsTimestamptz_IsRequired() {
    var withDomain = SchemaLockSample.Create() with {
      Domains = [SchemaLockSample.NewDomain("foo.moment", "timestamptz")],
      Tables = [
        SchemaLockSample.Table("foo.bar") with {
          Columns = [
            SchemaLockSample.Column("id", "bigint"),
            SchemaLockSample.Column("at_utc", "foo.moment", policy: TimePolicy.Relative),
          ],
        },
      ],
    };
    var text = SchemaLockWriter.Write(withDomain);

    Assert.Equal(TimePolicy.Relative, SchemaLockParser.Parse(text).Tables[0].Columns[1].Policy);
    Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(text.Replace(", policy: relative", "", StringComparison.Ordinal)));
  }

  [Fact]
  public void Parse_ReadsThePrimaryKeyInKeyOrder() {
    var parsed = SchemaLockParser.Parse(_Valid);

    Assert.Equal(["bar_id"], parsed.Tables[0].PrimaryKey);
    Assert.Equal(["bar_id", "tag_id"], parsed.Tables[1].PrimaryKey);
  }

  [Fact]
  public void Parse_KeyOrderIsNotColumnOrder() {
    var parsed = SchemaLockParser.Parse(_Valid.Replace("primary_key: [bar_id, tag_id]", "primary_key: [tag_id, bar_id]", StringComparison.Ordinal));

    Assert.Equal(["tag_id", "bar_id"], parsed.Tables[1].PrimaryKey);
  }

  [Fact]
  public void Parse_TableWithoutAPrimaryKey_HasAnEmptyKey() {
    var parsed = SchemaLockParser.Parse(_Valid.Replace("    primary_key: [tag_id]\n    primary_key_name: pk_tag\n", "", StringComparison.Ordinal));

    Assert.Empty(parsed.Tables[2].PrimaryKey);
  }

  [Fact]
  public void Parse_ReadsForeignKeys() {
    var keys = SchemaLockParser.Parse(_Valid).Tables[1].ForeignKeys;

    Assert.Equal(["link_bar", "link_tag"], keys.Select(key => key.Name));
    Assert.Equal(["bar_id"], keys[0].Columns);
    Assert.Equal("foo.bar", keys[0].ReferencedTable);
    Assert.Equal(["bar_id"], keys[0].ReferencedColumns);
    Assert.Equal(ReferentialAction.Cascade, keys[0].OnDelete);
    Assert.Equal(ReferentialAction.NoAction, keys[0].OnUpdate);
    Assert.False(keys[0].Deferrable);
    Assert.Equal(ReferentialAction.Restrict, keys[1].OnUpdate);
    Assert.True(keys[1].Deferrable);
    Assert.False(keys[1].InitiallyDeferred);
  }

  [Fact]
  public void Parse_OfEmptyLock_RoundTrips() {
    var text = SchemaLockWriter.Write(new() {
      Migrations = [],
      Schemas = [],
      Domains = [],
      Tables = [],
    });

    Assert.Equal(text, SchemaLockWriter.Write(SchemaLockParser.Parse(text)));
  }

  // The sample lock with its tables in another order and every kind of YAML layout the parser should accept.
  private static readonly string _Reformatted = $$"""
      version: 3
      migrations:
        - { version: 1, file: V001__foo.sql, checksum: {{SchemaLockSample.Aaa}} }
        - version: 3
          file: V003__bar.sql
          checksum: {{SchemaLockSample.Bbb}}
          accepted_checksums: [{{SchemaLockSample.Ccc}}]
      schemas:
        foo: Things and their tags
      domains:
        foo.slug:
          checks:
            - name: slug_format
              expression: "CHECK ((VALUE ~ '^[a-z0-9-]+$'::text))"
          comment: A URL slug
          type: text
        foo.short_slug: { type: foo.slug, checks: [{ name: short_slug_length, expression: "CHECK ((length(VALUE) <= 20))" }] }
        foo.coords: { type: "decimal(9, 6)", default_expression: "0::numeric", not_null: true }
        foo.tag_ids:
          type: smallint[]
      tables:
        foo.tag:
          columns:
            - { name: tag_id, type: smallint, generated_as: identity-by-default }
            - { name: price, type: "decimal(12, 2)", default_expression: "0.00" }
          primary_key_name: pk_tag
          primary_key: [tag_id]
        foo.link:
          primary_key_name: pk_link
          primary_key:
            - bar_id
            - tag_id
          foreign_keys:
            - name: link_bar
              columns: [bar_id]
              references: foo.bar
              referenced_columns: [bar_id]
              on_delete: cascade
            - { on_update: restrict, name: link_tag, columns: [tag_id], references: foo.tag, referenced_columns: [tag_id], deferrable: true }
          columns:
            - { name: bar_id, type: bigint }
            - { name: tag_id, type: smallint }
            - { name: flags, type: "smallint[]", nullable: true }
            - { name: tags, type: foo.tag_ids, nullable: true }
        foo.bar:
          primary_key: [bar_id]
          primary_key_name: pk_bar
          comment: A bar
          indexes:
            - name: ix_bar_created
              definition: CREATE INDEX ix_bar_created ON foo.bar USING btree (created_utc)
          checks:
            - { expression: "CHECK ((name != ''::text))", name: ck_bar_name }
          unique:
            - name: uq_bar_slug
              columns: [slug]
              definition: UNIQUE (slug)
          columns:
            - name: bar_id
              type: bigint
              generated_as: identity-always
            - { name: slug, type: foo.short_slug, comment: Public identifier }
            - { name: name, type: text }
            - { type: text, name: note, nullable: true }
            - { name: created_utc, type: timestamptz, policy: relative, default_expression: now() }
            - { name: published_utc, type: timestamptz, nullable: true, policy: absolute }
            - { name: lat, type: foo.coords, declared_nullable: true }
            - { name: slug_length, type: integer, generated_as: expression-stored, generated_expression: length(slug) }
      """;

  [Fact]
  public void Parse_DifferentFormatting_WritesBackTheCanonicalText() {
    var parsed = SchemaLockParser.Parse(_Reformatted);

    // The tables come back in the order they were written: sorting them is the generator's job, not the parser's.
    Assert.Equal(["foo.tag", "foo.link", "foo.bar"], parsed.Tables.Select(table => table.Name));
    Assert.Equal(
      SchemaLockWriter.Write(SchemaLockSample.Create() with { Tables = [.. SchemaLockSample.Create().Tables.OrderByDescending(table => table.Name, StringComparer.Ordinal)] }),
      SchemaLockWriter.Write(parsed));
  }

  [Fact]
  public void RoundTrip_NamesThatNeedQuoting() {
    var schemaLock = SchemaLockSample.Create() with {
      Tables = [
        SchemaLockSample.NewTable("foo.bar") with {
          Columns = [
            SchemaLockSample.Column("my col: \"x\"", "text"),
            SchemaLockSample.Column("-dash", "text"),
            SchemaLockSample.Column("null", "text"),
          ],
          PrimaryKey = [],
          PrimaryKeyName = null,
        },
      ],
    };

    var text = SchemaLockWriter.Write(schemaLock);
    var parsed = SchemaLockParser.Parse(text);

    Assert.Equal(["my col: \"x\"", "-dash", "null"], parsed.Tables[0].Columns.Select(column => column.Name));
    Assert.Equal(text, SchemaLockWriter.Write(parsed));
  }

  [Theory]
  [InlineData("migrations:\n", "extra: 1\nmigrations:\n")]
  [InlineData("migrations:\n", "migrations: nope\n")]
  [InlineData("tables:\n", "tablez:\n")]
  [InlineData("file: V001__foo.sql", "file: V002__foo.sql")]
  [InlineData("file: V001__foo.sql", "file: foo.sql")]
  [InlineData("  - version: 3\n    file: V003__bar.sql", "  - version: 1\n    file: V001__bar.sql")]
  [InlineData("    checksum: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "    checksum: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
  [InlineData("    checksum: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "    checksum: abc")]
  [InlineData("      - cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", "      - nope")]
  [InlineData("  foo.link:", "  foolink:")]
  [InlineData("  foo.link:", "  foo.bar:")]
  [InlineData("  foo.tag:\n    columns:", "  foo.tag:\n    colums:")]
  [InlineData("  foo.tag:\n    columns:\n      - { name: tag_id, type: smallint, generated_as: identity-by-default }\n      - { name: price, type: \"decimal(12, 2)\", default_expression: 0.00 }\n    primary_key: [tag_id]", "  foo.tag:\n    columns: []")]
  [InlineData("name: name, type: text", "name: name, type: interval")]
  [InlineData("nullable: true, policy", "nullable: maybe, policy")]
  [InlineData("primary_key: [bar_id, tag_id]", "primary_key: [bar_id, nope]")]
  [InlineData("primary_key: [bar_id, tag_id]", "primary_key: [bar_id, bar_id]")]
  [InlineData("primary_key: [bar_id, tag_id]", "primary_key: []")]
  [InlineData("primary_key: [bar_id, tag_id]", "primary_key: nope")]
  [InlineData("{ name: bar_id, type: bigint }", "{ name: bar_id, type: bigint, nullable: true }")]
  [InlineData("    primary_key: [tag_id]", "    primary_key: [tag_id]\n    primary_key: [tag_id]")]
  [InlineData("{ name: bar_id, type: bigint }", "{ name: bar_id, type: bigint, primary_key: 1 }")]
  [InlineData("default_expression: \"now()\", policy: relative", "default_expression: \"now()\"")]
  [InlineData("name: name, type: text", "name: name, type: text, policy: relative")]
  [InlineData("policy: relative", "policy: sometimes")]
  [InlineData("generated_as: identity-always", "generated_as: sometimes")]
  [InlineData("generated_as: identity-always", "generated: identity-always")]
  [InlineData("generated_as: expression-stored", "generated_as: stored")]
  [InlineData("{ name: slug, type: foo.short_slug,", "{ name: slug, type: foo.nope,")]
  [InlineData("{ name: lat, type: foo.coords, declared_nullable: true }", "{ name: lat, type: foo.coords, declared_nullable: true, policy: relative }")]
  [InlineData("{ name: lat, type: foo.coords, declared_nullable: true }", "{ name: lat, type: foo.coords, nullable: true, declared_nullable: true }")]
  [InlineData("{ name: tags, type: foo.tag_ids,", "{ name: tags, type: \"foo.tag_ids[]\",")]
  [InlineData("type: \"decimal(12, 2)\"", "type: \"decimal(12,2)\"")]
  [InlineData("type: \"decimal(12, 2)\"", "type: \"numeric(12, 2)\"")]
  [InlineData("type: \"decimal(12, 2)\"", "type: \"timestamp with time zone\"")]
  [InlineData("  foo.slug:\n    type: text", "  foo.slug:\n    type: interval")]
  [InlineData("  foo.slug:\n    type: text", "  foo.slug:\n    type: foo.short_slug")]
  [InlineData("    type: \"decimal(9, 6)\"", "    type: foo.nope")]
  [InlineData("    type: \"decimal(9, 6)\"", "    typo: \"decimal(9, 6)\"")]
  [InlineData("  foo.coords:", "  coords:")]
  [InlineData("  foo.coords:", "  foo.slug:")]
  [InlineData("  foo.slug:\n    type: text\n    comment: \"A URL slug\"\n    checks:", "  foo.slug:\n    type: text\n    comment: \"A URL slug\"\n    check:")]
  [InlineData("{ name: slug_format, expression:", "{ name: slug_format, text:")]
  [InlineData("expression: \"CHECK ((length(VALUE) <= 20))\"", "expression: \"\"")]
  [InlineData("domains:\n", "domains: nope\n")]
  [InlineData("schemas:\n  foo: \"Things and their tags\"", "schemas:\n  foo.x: \"Things and their tags\"")]
  [InlineData("schemas:\n  foo: \"Things and their tags\"", "schemas:\n  foo: \"\"")]
  [InlineData("schemas:\n  foo: \"Things and their tags\"", "schemas:\n  foo: [a, b]")]
  [InlineData("not_null: true", "not_null: maybe")]
  [InlineData("default_expression: \"0::numeric\"", "default_expression: \"\"")]
  [InlineData("comment: \"A URL slug\"", "comment: \"\"")]
  [InlineData("    type: \"decimal(9, 6)\"", "    type: \"decimal(9, 6)\"\n    color: red")]
  [InlineData("    comment: \"A bar\"", "    comment: \"\"")]
  [InlineData("    primary_key_name: pk_bar\n", "")]
  [InlineData("    primary_key: [tag_id]\n", "")]
  [InlineData("generated_as: identity-always }", "generated_as: identity-always, default_expression: \"1\" }")]
  [InlineData("generated_as: identity-always }", "generated_as: identity-always, generated_expression: \"1\" }")]
  [InlineData(", generated_expression: \"length(slug)\"", "")]
  [InlineData("generated_expression: \"length(slug)\"", "generated_expression: \"\"")]
  [InlineData("generated_as: expression-stored,", "generated_as: expression-stored, default_expression: \"1\",")]
  [InlineData("default_expression: \"now()\", policy", "default_expression: \"\", policy")]
  [InlineData("comment: \"Public identifier\"", "comment: \"\"")]
  [InlineData("columns: [slug], definition", "columns: [nope], definition")]
  [InlineData("columns: [slug], definition", "columns: [], definition")]
  [InlineData("definition: \"UNIQUE (slug)\"", "definition: \"\"")]
  [InlineData("{ name: uq_bar_slug,", "{ nme: uq_bar_slug,")]
  [InlineData("    unique:\n", "    unique: nope\n")]
  [InlineData("expression: \"CHECK ((name != ''::text))\"", "expression: \"\"")]
  [InlineData("{ name: ix_bar_created, definition:", "{ name: ix_bar_created, def:")]
  [InlineData("definition: \"CREATE INDEX ix_bar_created ON foo.bar USING btree (created_utc)\"", "definition: \"\"")]
  [InlineData("{ name: note,", "{ name: name,")]
  [InlineData("{ name: name,", "{ name: name, color: red,")]
  [InlineData("references: foo.bar,", "references: foo.nope,")]
  [InlineData("references: foo.bar,", "references: bar,")]
  [InlineData("referenced_columns: [bar_id]", "referenced_columns: [nope]")]
  [InlineData("referenced_columns: [bar_id]", "referenced_columns: [bar_id, tag_id]")]
  [InlineData("referenced_columns: [bar_id]", "referenced_columns: []")]
  [InlineData("columns: [bar_id], references", "columns: [nope], references")]
  [InlineData("name: link_tag,", "name: link_bar,")]
  [InlineData("on_update: restrict, deferrable: true", "on_update: restrict, deferred: true")]
  [InlineData("on_delete: cascade", "on_delete: explode")]
  [InlineData("on_update: restrict", "on_update: Restrict")]
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
  public void Parse_OldVersionKey_IsIgnoredAndTheVersionIsTheNewestMigration() {
    var text = _Valid.Replace("migrations:", "version: 99\nmigrations:", StringComparison.Ordinal);

    var parsed = SchemaLockParser.Parse(text);

    Assert.Equal(3, parsed.Version);
    Assert.DoesNotContain("version: 99", SchemaLockWriter.Write(parsed), StringComparison.Ordinal);
  }

  [Fact]
  public void Parse_Error_NamesTheFile() {
    var exception = Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse("migrations: nope\ntables: {}"));

    Assert.StartsWith("schema.lock.yaml:", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Parse_ForeignKeyError_NamesTheTableAndKey() {
    var exception = Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(_Valid.Replace("references: foo.bar,", "references: foo.nope,", StringComparison.Ordinal)));

    Assert.Contains("foo.link foreign key link_bar", exception.Message, StringComparison.Ordinal);
    Assert.Contains("foo.nope", exception.Message, StringComparison.Ordinal);
  }
}
