namespace JoshOlson.Database.Migrate;

// What `seeds:migrate` did: the lock's path, the version it said before (null if there was no lock) and says now, and
// whether the file changed. A run that finds the lock already right is not a change.
public sealed record MigrateResult {
  public required string Path { get; init; }
  public required int? PreviousVersion { get; init; }
  public required int Version { get; init; }
  public required bool Changed { get; init; }
}
