namespace JoshOlson.Database.Schema;

// A domain: the type it is built on, which may be a base type, an array, or another domain, and its CHECK constraints by
// name. A column that uses the domain says only the domain's name.
public sealed record Domain {
  public required string Name { get; init; }
  public required ColumnType Type { get; init; }
  public required List<CheckConstraint> Checks { get; init; }
}
