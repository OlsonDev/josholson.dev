namespace JoshOlson.Database.Catalog;

// What the reader looks at. The system schemas (`pg_catalog`, `information_schema`, `pg_toast`, and the like) are never
// read. `ExcludedSchemas` lists others to skip, which a project sets for itself, and `OnlySchemas`, when set, narrows
// the read to just those.
public sealed record CatalogOptions {
  public List<string> ExcludedSchemas { get; init; } = ["public"];
  public List<string>? OnlySchemas { get; init; }
}
