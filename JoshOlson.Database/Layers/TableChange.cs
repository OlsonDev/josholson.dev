namespace JoshOlson.Database.Layers;

// What a layer does to one table. A row maps column names to canonical text, which Postgres casts on insert.
// `Deletes` rows hold only the primary key columns.
public sealed record TableChange {
  public required string Table { get; init; }
  public required List<Dictionary<string, string?>> Upserts { get; init; }
  public required List<Dictionary<string, string?>> Deletes { get; init; }
}
