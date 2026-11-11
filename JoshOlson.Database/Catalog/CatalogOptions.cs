namespace JoshOlson.Database.Catalog;

// What the reader looks at. The system schemas (`pg_catalog`, `information_schema`, `pg_toast`, and the like) are never
// read. `ExcludedSchemas` lists others to skip, which a project sets for itself (here `public`, and `seed`, which the
// seed tool adds to every database it builds and which no migration owns), and `OnlySchemas`, when set, narrows the
// read to just those.
public sealed record CatalogOptions {
  public List<string> ExcludedSchemas { get; init; } = [
    "public",
    "seed",
  ];
  public List<string>? OnlySchemas { get; init; }
}
