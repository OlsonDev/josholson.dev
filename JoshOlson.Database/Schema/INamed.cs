namespace JoshOlson.Database.Schema;

// Something the schema model lists by name: a schema, domain, table, or one of a table's constraints or indexes.
// Lists of them are kept in ordinal name order, so the order does not depend on a collation.
public interface INamed {
  public string Name { get; }
}
