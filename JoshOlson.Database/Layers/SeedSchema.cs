using JoshOlson.Database.Locks;
using JoshOlson.Database.Schema;

namespace JoshOlson.Database.Layers;

// The lock as the layer parser needs it: tables by name, each column's type resolved once. A column whose type does not
// resolve cannot be in a lock the parser accepted, so one here means the lock was built by hand and is refused.
public sealed class SeedSchema {
  private readonly Dictionary<string, SeedTable> _tables = new(StringComparer.Ordinal);
  private readonly SchemaLock _lock;

  public SeedSchema(SchemaLock schemaLock) {
    _lock = schemaLock;
    var domains = new Dictionary<string, Domain>(StringComparer.Ordinal);
    foreach (var domain in schemaLock.Domains) {
      domains[domain.Name] = domain;
    }
    foreach (var table in schemaLock.Tables) {
      var columns = new Dictionary<string, SeedColumn>(table.Columns.Count, StringComparer.Ordinal);
      foreach (var column in table.Columns) {
        var type = DomainResolver.Resolve(column.Type, domains)
          ?? throw new SeedFormatException($"{table.Name}.{column.Name} has type '{column.Type}', which does not resolve to a seed type.");
        columns[column.Name] = new(column, type);
      }
      _tables[table.Name] = new(table, columns);
    }
  }

  public SeedTable? Find(string table)
    => _tables.GetValueOrDefault(table);

  // The tables in an order rows can be inserted in, each after the tables its foreign keys point at (`SchemaLock.InsertOrder`).
  public List<SeedTable> InsertOrder()
    => [.. _lock.InsertOrder().Select(table => _tables[table.Name])];
}
