namespace JoshOlson.Database.Schema;

// The comment on a database schema (`COMMENT ON SCHEMA`), by the schema's name.
public sealed record SchemaComment : INamed {
  public required string Name { get; init; }
  public required string Comment { get; init; }
}
