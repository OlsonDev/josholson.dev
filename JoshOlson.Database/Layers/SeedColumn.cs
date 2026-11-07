using JoshOlson.Database.Schema;

namespace JoshOlson.Database.Layers;

// A column with its type followed down to a base type, so a value can be read without asking the domains again.
public sealed record SeedColumn(Column Column, ResolvedType Type);
