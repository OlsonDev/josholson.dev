using JoshOlson.Database.Schema;

namespace JoshOlson.Database.Locks;

// One lock that exercises everything the file format has: an accepted checksum; domains over a base type, over another
// domain, with a modifier and over an array, with and without checks; a nullable column; identity and expression
// columns; a composite primary key; both time policies; array and `decimal` types; and foreign keys with and without
// actions and deferral. Shared by the writer and parser tests.
internal static class SchemaLockSample {
  public static readonly string Aaa = new('a', 64);
  public static readonly string Bbb = new('b', 64);
  public static readonly string Ccc = new('c', 64);

  public static SchemaLock Create()
    => new() {
      Version = 3,
      Migrations = [
        new() {
          Version = 1,
          File = "V001__foo.sql",
          Checksum = Aaa,
          AcceptedChecksums = [],
        },
        new() {
          Version = 3,
          File = "V003__bar.sql",
          Checksum = Bbb,
          AcceptedChecksums = [Ccc],
        },
      ],
      Domains = [
        NewDomain("foo.slug", "text", ("slug_format", "CHECK ((VALUE ~ '^[a-z0-9-]+$'::text))")),
        NewDomain("foo.short_slug", "foo.slug", ("short_slug_length", "CHECK ((length(VALUE) <= 20))")),
        NewDomain("foo.coords", "decimal(9, 6)"),
        NewDomain("foo.tag_ids", "smallint[]"),
      ],
      Tables = Tables(),
    };

  private static List<Table> Tables()
    => [
      new() {
        Name = "foo.bar",
        Columns = [
          Column("bar_id", "bigint", generatedAs: GeneratedAs.IdentityAlways),
          Column("slug", "foo.short_slug"),
          Column("name", "text"),
          Column("note", "text", nullable: true),
          Column("created_utc", "timestamptz", policy: TimePolicy.Relative),
          Column("published_utc", "timestamptz", nullable: true, policy: TimePolicy.Absolute),
          Column("lat", "foo.coords", nullable: true),
          Column("slug_length", "integer", generatedAs: GeneratedAs.ExpressionStored),
        ],
        PrimaryKey = ["bar_id"],
        ForeignKeys = [],
      },
      new() {
        Name = "foo.link",
        Columns = [
          Column("bar_id", "bigint"),
          Column("tag_id", "smallint"),
          Column("flags", "smallint[]", nullable: true),
          Column("tags", "foo.tag_ids", nullable: true),
        ],
        PrimaryKey = ["bar_id", "tag_id"],
        ForeignKeys = [
          ForeignKey("link_bar", ["bar_id"], "foo.bar", ["bar_id"], onDelete: ReferentialAction.Cascade),
          ForeignKey("link_tag", ["tag_id"], "foo.tag", ["tag_id"], onUpdate: ReferentialAction.Restrict, deferrable: true),
        ],
      },
      new() {
        Name = "foo.tag",
        Columns = [
          Column("tag_id", "smallint", generatedAs: GeneratedAs.IdentityByDefault),
          Column("price", "decimal(12, 2)"),
        ],
        PrimaryKey = ["tag_id"],
        ForeignKeys = [],
      },
    ];

  public static Domain NewDomain(string name, string type, params (string Name, string Expression)[] checks)
    => new() {
      Name = name,
      Type = ColumnType.Parse(type),
      Checks = [.. checks.Select(check => new CheckConstraint { Name = check.Name, Expression = check.Expression })],
    };

  public static Column Column(
    string name,
    string type,
    bool nullable = false,
    GeneratedAs? generatedAs = null,
    TimePolicy? policy = null)
    => new() {
      Name = name,
      Type = ColumnType.Parse(type),
      Nullable = nullable,
      GeneratedAs = generatedAs,
      Policy = policy,
    };

  public static ForeignKey ForeignKey(
    string name,
    List<string> columns,
    string referencedTable,
    List<string> referencedColumns,
    ReferentialAction onDelete = ReferentialAction.NoAction,
    ReferentialAction onUpdate = ReferentialAction.NoAction,
    bool deferrable = false,
    bool deferred = false)
    => new() {
      Name = name,
      Columns = columns,
      ReferencedTable = referencedTable,
      ReferencedColumns = referencedColumns,
      OnDelete = onDelete,
      OnUpdate = onUpdate,
      Deferrable = deferrable,
      InitiallyDeferred = deferred,
    };

  public static Table Table(string name, params ForeignKey[] foreignKeys)
    => new() {
      Name = name,
      Columns = [Column("id", "bigint")],
      PrimaryKey = ["id"],
      ForeignKeys = [.. foreignKeys],
    };
}
