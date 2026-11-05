using JoshOlson.Database.Schema;

namespace JoshOlson.Database.Catalog;

// Everything the queries returned, to be assembled into the model, keyed by the catalog oid of the table or domain: a
// dictionary where there is one row per key, and a dictionary of lists where there can be several (a table's columns).
internal sealed record CatalogRows {
  public required List<SchemaComment> Schemas { get; init; }
  public required Dictionary<long, Table> Tables { get; init; }
  public required Dictionary<long, Domain> Domains { get; init; }
  public required Dictionary<long, List<CheckConstraint>> DomainChecks { get; init; }
  public required Dictionary<long, List<Column>> Columns { get; init; }
  public required Dictionary<long, UniqueConstraint> PrimaryKeys { get; init; }
  public required Dictionary<long, List<UniqueConstraint>> UniqueConstraints { get; init; }
  public required Dictionary<long, List<CheckConstraint>> Checks { get; init; }
  public required Dictionary<long, List<ForeignKey>> ForeignKeys { get; init; }
  public required Dictionary<long, List<TableIndex>> Indexes { get; init; }
}
