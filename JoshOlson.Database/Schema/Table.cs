namespace JoshOlson.Database.Schema;

// A table's columns, in catalog order (which is the order a seed is exported in); its primary key as column names in key
// order with the constraint's name (empty and null when the table has none); and its unique constraints, check
// constraints, foreign keys, and indexes by name. Only indexes that do not back a constraint are listed. The tables'
// own constraints and indexes are what the docs show and what `seeds:accept` compares. `Ignored` is set by `@seed ignore` in
// the table's comment: its rows are runtime data (sessions, credentials), not seed data, so no layer may name it and an
// export leaves it out.
public sealed record Table : INamed {
  public required string Name { get; init; }
  public required string? Comment { get; init; }
  public required bool Ignored { get; init; }
  public required List<Column> Columns { get; init; }
  public required List<string> PrimaryKey { get; init; }
  public required string? PrimaryKeyName { get; init; }
  public required List<UniqueConstraint> UniqueConstraints { get; init; }
  public required List<CheckConstraint> Checks { get; init; }
  public required List<ForeignKey> ForeignKeys { get; init; }
  public required List<TableIndex> Indexes { get; init; }
}
