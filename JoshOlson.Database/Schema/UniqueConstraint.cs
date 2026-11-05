namespace JoshOlson.Database.Schema;

// A UNIQUE constraint: its columns in key order, and its definition as the catalog prints it (`UNIQUE (foo, bar)`, with
// any `DEFERRABLE` or `NULLS NOT DISTINCT`), kept as text and never interpreted.
public sealed record UniqueConstraint {
  public required string Name { get; init; }
  public required List<string> Columns { get; init; }
  public required string Definition { get; init; }
}
