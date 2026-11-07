namespace JoshOlson.Database.Layers;

// What a layer does to one table. A row maps column names to typed values, and a NULL is a C# `null`. `Deletes` rows
// hold only the primary key columns, and every row holds all of them.
public sealed record TableChange {
  public required string Table { get; init; }
  public required List<Dictionary<string, SeedValue?>> Upserts { get; init; }
  public required List<Dictionary<string, SeedValue?>> Deletes { get; init; }
}
