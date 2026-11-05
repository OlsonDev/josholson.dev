using JoshOlson.Database.Schema;

namespace JoshOlson.Database.Catalog;

// What the reader found, in the order the lock writes it: schema comments, domains, and tables by name.
public sealed record CatalogSchema {
  public required List<SchemaComment> Schemas { get; init; }
  public required List<Domain> Domains { get; init; }
  public required List<Table> Tables { get; init; }
}
