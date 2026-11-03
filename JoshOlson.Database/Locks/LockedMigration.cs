namespace JoshOlson.Database.Locks;

// A versioned migration the seeds were migrated against. `Checksum` is the SHA-256 of the file as it was then.
// `AcceptedChecksums` are the SHA-256 checksums of later edits that `seeds:accept` verified leave the seeds unaffected
// (a comment, whitespace), so a checkout with either version of the file still matches the lock.
public sealed record LockedMigration {
  public required int Version { get; init; }
  public required string File { get; init; }
  public required string Checksum { get; init; }
  public required List<string> AcceptedChecksums { get; init; }

  public bool Matches(string checksum)
    => string.Equals(checksum, Checksum, StringComparison.Ordinal) || AcceptedChecksums.Contains(checksum, StringComparer.Ordinal);
}
