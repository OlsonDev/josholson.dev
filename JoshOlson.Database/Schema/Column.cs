namespace JoshOlson.Database.Schema;

// One column as the seeds saw it. `Type` is the type as the column was declared, which is a domain's name when it was
// declared with one (the lock's `domains` say what that domain is built on). `Nullable` is the effective nullability:
// false also when the column's domain is NOT NULL. `Policy` is set exactly when the type comes to `timestamptz`.
public sealed record Column {
  public required string Name { get; init; }
  public required ColumnType Type { get; init; }
  public required bool Nullable { get; init; }
  public required GeneratedAs? GeneratedAs { get; init; }
  public required TimePolicy? Policy { get; init; }
}
