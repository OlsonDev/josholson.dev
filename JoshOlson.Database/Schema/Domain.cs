namespace JoshOlson.Database.Schema;

// A domain: the type it is built on, which may be a base type, an array, or another domain, whether it is NOT NULL, its
// default expression and comment if it has them, and its CHECK constraints by name. A column that uses the domain says
// only the domain's name.
public sealed record Domain {
  public required string Name { get; init; }
  public required ColumnType Type { get; init; }
  public required bool NotNull { get; init; }
  public required string? DefaultExpression { get; init; }
  public required string? Comment { get; init; }
  public required List<CheckConstraint> Checks { get; init; }
}
