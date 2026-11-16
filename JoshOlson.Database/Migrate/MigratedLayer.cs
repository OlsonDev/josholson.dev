namespace JoshOlson.Database.Migrate;

// A layer `seeds:migrate` moved to the newest schema, and whether its file had to be rewritten.
public sealed record MigratedLayer(string Layer, bool Rewritten);
