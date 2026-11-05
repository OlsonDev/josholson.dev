namespace JoshOlson.Database.Schema;

// An index that does not back a constraint, with its definition as the catalog prints it (`CREATE INDEX ...`), kept as
// text and never interpreted.
public sealed record TableIndex {
  public required string Name { get; init; }
  public required string Definition { get; init; }
}
