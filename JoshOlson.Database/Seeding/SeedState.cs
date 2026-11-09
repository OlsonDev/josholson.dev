using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Seeding;

// The rows a chain of layers comes to: for each table, every row by its primary key (in key order), each a map of
// column to value. A column a row never mentions is absent and takes its default when the row is inserted.
public sealed class SeedState {
  public Dictionary<string, SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>>> Tables { get; } = new(StringComparer.Ordinal);

  public int RowCount
    => Tables.Values.Sum(rows => rows.Count);
}
