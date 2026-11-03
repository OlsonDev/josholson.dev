namespace JoshOlson.Database.Locks;

// One column as the seeds saw it. `PrimaryKeyPosition` is 1-based within the primary key and null for other columns,
// and `Policy` is set exactly for `timestamptz` columns.
public sealed record LockedColumn {
  public required string Name { get; init; }
  public required ColumnType Type { get; init; }
  public required bool Nullable { get; init; }
  public required int? PrimaryKeyPosition { get; init; }
  public required TimePolicy? Policy { get; init; }
}
