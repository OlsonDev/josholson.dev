namespace JoshOlson.Database.Locks;

// A versioned migration the seeds were migrated against. `Checksum` is the SHA-256 of the file as it was then.
public sealed record LockedMigration {
  public required int Version { get; init; }
  public required string File { get; init; }
  public required string Checksum { get; init; }

  public bool Matches(string checksum)
    => string.Equals(checksum, Checksum, StringComparison.Ordinal);
}
