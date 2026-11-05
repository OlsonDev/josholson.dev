namespace JoshOlson.Database.Schema;

// A table's columns, in catalog order (which is the order a seed is exported in), its primary key as column names in key
// order (empty when the table has none), and its foreign keys by name.
public sealed record Table {
  public required string Name { get; init; }
  public required List<Column> Columns { get; init; }
  public required List<string> PrimaryKey { get; init; }
  public required List<ForeignKey> ForeignKeys { get; init; }
}
