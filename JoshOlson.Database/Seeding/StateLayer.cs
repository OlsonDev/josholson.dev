using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Seeding;

// A state as a layer that inserts every one of its rows, which is what a database read with `SeedStateReader` is when
// it is the base of an export: merging it with the exported layer must give the database's rows back.
public static class StateLayer {
  public static SeedLayer Of(SeedState state, string name)
    => new() {
      Name = name,
      Extends = null,
      Description = name,
      Tables = [
        .. state.Tables
          .Where(table => table.Value.Count > 0)
          .OrderBy(table => table.Key, StringComparer.Ordinal)
          .Select(table => new TableChange {
            Table = table.Key,
            Inserts = [.. table.Value.Values.Select(row => new Dictionary<string, SeedValue?>(row, StringComparer.Ordinal))],
            Updates = [],
            Deletes = [],
          }),
      ],
    };
}
