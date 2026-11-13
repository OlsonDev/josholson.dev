using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Migrate;

// The text of the small schema `FakeSeedRepository` works on: its migrations, and its layers as builders, so a
// variation (the layers after the note migration, a child with one more row) is an argument and not another literal. The
// migrations' text is fixed, because a schema template's name is a hash of it: the same text is the same template on
// every run.
internal static class FooSchema {
  // `foo.bar` with an identity key and a name.
  public const string BarFile = "V001__bar.sql";

  public const string Bar = """
    CREATE SCHEMA foo;
    CREATE TABLE foo.bar (
      bar_id INT GENERATED ALWAYS AS IDENTITY,
      name TEXT NOT NULL,
      CONSTRAINT pk_bar PRIMARY KEY (bar_id)
    );

    """;

  // `foo.kind`, which `foo.bar` points at, and the repeatable that fills it.
  public const string KindFile = "V001__kind.sql";

  public const string Kind = """
    CREATE SCHEMA foo;
    CREATE TABLE foo.kind (kind_id INT NOT NULL, label TEXT NOT NULL, CONSTRAINT pk_kind PRIMARY KEY (kind_id));
    CREATE TABLE foo.bar (
      bar_id INT GENERATED ALWAYS AS IDENTITY,
      kind_id INT NOT NULL,
      CONSTRAINT pk_bar PRIMARY KEY (bar_id),
      CONSTRAINT fk_bar_kind FOREIGN KEY (kind_id) REFERENCES foo.kind (kind_id)
    );

    """;

  public const string KindRepeatableFile = "R__kind.sql";

  public const string KindRepeatable =
    "INSERT INTO foo.kind (kind_id, label) VALUES (1, 'A') ON CONFLICT (kind_id) DO NOTHING;\n";

  // The second migration of either schema: a column that is not null and has a default.
  public const string NoteFile = "V002__note.sql";

  // For the kind schema, with the repeatable as it is from then on: it fills a column the first version does not have,
  // so it cannot apply there and the reference row is missing until the migrations have run.
  public const string KindNote = "ALTER TABLE foo.kind ADD COLUMN note TEXT NOT NULL DEFAULT 'n';\n";

  public const string KindNoteRepeatable =
    "INSERT INTO foo.kind (kind_id, label, note) VALUES (1, 'A', 'z') "
    + "ON CONFLICT (kind_id) DO UPDATE SET note = excluded.note;\n";

  // For the bar schema. Another default is what an edit of the migration would write.
  public static string Note(string @default = "x")
    => $"ALTER TABLE foo.bar ADD COLUMN note TEXT NOT NULL DEFAULT '{@default}';\n";

  // One line of a `foo.bar` insert list. `note` is the column's value once the note migration has run.
  public static string Insert(int barId, string name, string? note = null)
    => $"      - {{ bar_id: {barId.ToInvariantString()}, name: {name}{(note is null ? "" : $", note: {note}")} }}\n";

  // The root layer, `base`.
  public static string BaseLayer(string? note = null)
    => $"description: Base\ntables:\n  foo.bar:\n    insert:\n{Insert(1, "One", note)}{Insert(2, "Two", note)}";

  // `child`, which extends `base`. `extraInserts` is more lines from `Insert`, after its own.
  public static string ChildLayer(string? note = null, string extraInserts = "")
    => "description: Child\nextends: base\ntables:\n  foo.bar:\n    insert:\n"
      + $"{Insert(1001, "Three", note)}{extraInserts}    update:\n      - {{ bar_id: 1, name: Uno }}\n";

  // `extra`, a second child of `base`, which no test has the tool write.
  public static string ExtraLayer(string? note = null)
    => $"description: Extra\nextends: base\ntables:\n  foo.bar:\n    insert:\n{Insert(1001, "Extra", note)}";

  // The root layer of the kind schema, with one row that points at a kind.
  public static string KindLayer(int kindId)
    => "description: Base\ntables:\n  foo.bar:\n    insert:\n"
      + $"      - {{ bar_id: 1, kind_id: {kindId.ToInvariantString()} }}\n";
}
