namespace JoshOlson.Database.Schema;

// One column as the seeds saw it. `Type` is the type as the column was declared, which is a domain's name when it was
// declared with one (the lock's `domains` say what that domain is built on). `Nullable` is the effective nullability:
// false also when the column's domain is NOT NULL. `Policy` is set exactly when the type comes to `timestamptz`.
// `DefaultExpression` is the DEFAULT expression as the catalog prints it, and `GeneratedExpression` the expression of a column
// generated from one (`GeneratedAs` is `ExpressionStored` or `ExpressionVirtual`); an identity column has neither.
// `Comment` is the column's comment as written, including any `@seed` directive.
public sealed record Column {
  public required string Name { get; init; }
  public required ColumnType Type { get; init; }
  public required bool Nullable { get; init; }
  public required GeneratedAs? GeneratedAs { get; init; }
  public required string? GeneratedExpression { get; init; }
  public required string? DefaultExpression { get; init; }
  public required TimePolicy? Policy { get; init; }
  public required string? Comment { get; init; }
}
