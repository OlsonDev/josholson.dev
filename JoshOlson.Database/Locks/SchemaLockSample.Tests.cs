using JoshOlson.Database.Schema;

namespace JoshOlson.Database.Locks;

// One lock that exercises everything the file format has: an accepted checksum; a schema comment; domains over a base
// type, over another domain, with a modifier and over an array, with and without checks, defaults, and comments; a
// nullable column; identity and expression columns; defaults and comments; a composite primary key; unique and check
// constraints; both time policies; array and `decimal` types; foreign keys with and without actions and deferral; and an
// index. Shared by the writer and parser tests.
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
      Schemas = [
        new() {
          Name = "foo",
          Comment = "Things and their tags",
        },
      ],
      Domains = Domains(),
      Tables = Tables(),
    };

  private static List<Domain> Domains()
    => [
      NewDomain("foo.slug", "text", ("slug_format", "CHECK ((VALUE ~ '^[a-z0-9-]+$'::text))")) with { Comment = "A URL slug" },
      NewDomain("foo.short_slug", "foo.slug", ("short_slug_length", "CHECK ((length(VALUE) <= 20))")),
      NewDomain("foo.coords", "decimal(9, 6)") with { NotNull = true, DefaultExpression = "0::numeric" },
      NewDomain("foo.tag_ids", "smallint[]"),
    ];

  private static List<Table> Tables()
    => [
      NewTable("foo.bar") with {
        Comment = "A bar",
        Columns = [
          Column("bar_id", "bigint", generatedAs: GeneratedAs.IdentityAlways),
          Column("slug", "foo.short_slug") with { Comment = "Public identifier" },
          Column("name", "text"),
          Column("note", "text", nullable: true),
          Column("created_utc", "timestamptz", policy: TimePolicy.Relative) with { DefaultExpression = "now()" },
          Column("published_utc", "timestamptz", nullable: true, policy: TimePolicy.Absolute),
          Column("lat", "foo.coords") with { DeclaredNullable = true },
          Column("slug_length", "integer", generatedAs: GeneratedAs.ExpressionStored) with { GeneratedExpression = "length(slug)" },
        ],
        PrimaryKey = ["bar_id"],
        PrimaryKeyName = "pk_bar",
        UniqueConstraints = [
          new() {
            Name = "uq_bar_slug",
            Columns = ["slug"],
            Definition = "UNIQUE (slug)",
          },
        ],
        Checks = [
          new() {
            Name = "ck_bar_name",
            Expression = "CHECK ((name != ''::text))",
          },
        ],
        Indexes = [
          new() {
            Name = "ix_bar_created",
            Definition = "CREATE INDEX ix_bar_created ON foo.bar USING btree (created_utc)",
          },
        ],
      },
      NewTable("foo.link") with {
        Columns = [
          Column("bar_id", "bigint"),
          Column("tag_id", "smallint"),
          Column("flags", "smallint[]", nullable: true),
          Column("tags", "foo.tag_ids", nullable: true),
        ],
        PrimaryKey = ["bar_id", "tag_id"],
        PrimaryKeyName = "pk_link",
        ForeignKeys = [
          ForeignKey("link_bar", ["bar_id"], "foo.bar", ["bar_id"], onDelete: ReferentialAction.Cascade),
          ForeignKey("link_tag", ["tag_id"], "foo.tag", ["tag_id"], onUpdate: ReferentialAction.Restrict, deferrable: true),
        ],
      },
      NewTable("foo.tag") with {
        Columns = [
          Column("tag_id", "smallint", generatedAs: GeneratedAs.IdentityByDefault),
          Column("price", "decimal(12, 2)") with { DefaultExpression = "0.00" },
        ],
        PrimaryKey = ["tag_id"],
        PrimaryKeyName = "pk_tag",
      },
    ];

  public static Domain NewDomain(string name, string type, params (string Name, string Expression)[] checks)
    => new() {
      Name = name,
      Type = type,
      NotNull = false,
      DefaultExpression = null,
      Comment = null,
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
      Type = type,
      DeclaredNullable = nullable,
      Nullable = nullable,
      GeneratedAs = generatedAs,
      GeneratedExpression = null,
      DefaultExpression = null,
      Policy = policy,
      Comment = null,
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

  // A table with one `id` column that is its primary key, nothing else; tests add what they need with `with`.
  public static Table NewTable(string name)
    => new() {
      Name = name,
      Comment = null,
      Ignored = false,
      Columns = [Column("id", "bigint")],
      PrimaryKey = ["id"],
      PrimaryKeyName = $"pk_{name.Split('.')[1]}",
      UniqueConstraints = [],
      Checks = [],
      ForeignKeys = [],
      Indexes = [],
    };

  public static Table Table(string name, params ForeignKey[] foreignKeys)
    => NewTable(name) with { ForeignKeys = [.. foreignKeys] };
}
