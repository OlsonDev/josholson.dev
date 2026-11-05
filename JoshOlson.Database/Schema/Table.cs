namespace JoshOlson.Database.Locks;

// A table's columns, in catalog order (which is the order a seed is exported in).
public sealed record LockedTable {
  public required string Name { get; init; }
  public required List<LockedColumn> Columns { get; init; }
}
