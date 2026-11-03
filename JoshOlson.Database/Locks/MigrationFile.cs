namespace JoshOlson.Database.Locks;

// A versioned migration on disk: `V042__foo.sql` is version 42, with the SHA-256 of its bytes as written.
public sealed record MigrationFile {
  public required int Version { get; init; }
  public required string File { get; init; }
  public required string Checksum { get; init; }
}
