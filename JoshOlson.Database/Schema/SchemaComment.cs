namespace JoshOlson.Database.Schema;

// The comment on a database schema (`COMMENT ON SCHEMA`), by the schema's name.
public sealed record SchemaComment {
  public required string Name { get; init; }
  public required string Comment { get; init; }
}
