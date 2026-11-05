namespace JoshOlson.Database.Schema;

// What a column's type comes to once its domains are followed: the base type the values are written as, and whether they
// are a one-dimensional array of it.
public readonly record struct ResolvedType(SeedType Seed, bool IsArray);
