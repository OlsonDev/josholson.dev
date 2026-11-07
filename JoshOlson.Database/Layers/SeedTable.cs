using JoshOlson.Database.Schema;

namespace JoshOlson.Database.Layers;

// A table of the lock with each column resolved, by column name.
public sealed record SeedTable(Table Table, Dictionary<string, SeedColumn> Columns);
