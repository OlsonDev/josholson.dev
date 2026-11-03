namespace JoshOlson.Database.Layers;

// One YAML file in `seeds/`: a delta against its parent layer. The name is the file name without `.yaml`.
public sealed record SeedLayer {
  public required string Name { get; init; }
  public required string? Extends { get; init; }
  public required string Description { get; init; }
  public required List<TableChange> Tables { get; init; }

  public int RowCount
    => Tables.Sum(table => table.Upserts.Count + table.Deletes.Count);
}
