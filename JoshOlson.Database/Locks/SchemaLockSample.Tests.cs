namespace JoshOlson.Database.Locks;

// One lock that exercises everything the file format has: an accepted checksum, a nullable column, a composite primary
// key, both time policies, and an array type. Shared by the writer and parser tests.
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
      Tables = [
        new() {
          Name = "foo.bar",
          Columns = [
            Column("bar_id", "bigint", primaryKey: 1),
            Column("name", "text"),
            Column("note", "text", nullable: true),
            Column("created_utc", "timestamptz", policy: TimePolicy.Relative),
            Column("published_utc", "timestamptz", nullable: true, policy: TimePolicy.Absolute),
          ],
        },
        new() {
          Name = "foo.link",
          Columns = [
            Column("bar_id", "bigint", primaryKey: 1),
            Column("tag_id", "smallint", primaryKey: 2),
            Column("flags", "smallint[]", nullable: true),
          ],
        },
      ],
    };

  public static LockedColumn Column(string name, string type, bool nullable = false, int? primaryKey = null, TimePolicy? policy = null)
    => new() {
      Name = name,
      Type = ColumnType.Parse(type),
      Nullable = nullable,
      PrimaryKeyPosition = primaryKey,
      Policy = policy,
    };
}
