namespace JoshOlson.Database.Baking;

// A repeatable migration a schema template for an older version went without, and why (psql's error).
public sealed record SkippedRepeatable(string File, string Error);
