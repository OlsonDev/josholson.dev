namespace JoshOlson.Database.Layers;

// What a layer does to one table. A row maps column names to typed values, and a NULL is a C# `null`.
// - `Inserts` are new rows: each names every column that is not generated from an expression, and its key must not be a
//   row an earlier layer (or this one) already gives.
// - `Updates` change a row an earlier layer gives: each names the key and the columns that change, and no others.
// - `Deletes` remove one, and hold only the primary key columns.
// Every row holds the primary key columns.
public sealed record TableChange {
  public required string Table { get; init; }
  public required List<Dictionary<string, SeedValue?>> Inserts { get; init; }
  public required List<Dictionary<string, SeedValue?>> Updates { get; init; }
  public required List<Dictionary<string, SeedValue?>> Deletes { get; init; }
}
