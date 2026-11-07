namespace JoshOlson.Database.Schema;

// What a column's type comes to once its domains are followed: the base type the values are written as, the text inside
// that base type's parentheses (the precision and scale of a `decimal`), and whether the values are a one-dimensional
// array of it.
public readonly record struct ResolvedType(SeedType Seed, bool IsArray, string? Modifiers = null);
