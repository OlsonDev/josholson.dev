namespace JoshOlson.Database.Migrate;

// A versioned migration as Waypoint's history table records it: the version, the script's file name, and whether it
// ran to the end. Repeatable migrations are not versioned and are not listed.
public sealed record AppliedMigration {
  public required int Version { get; init; }
  public required string File { get; init; }
  public required bool Success { get; init; }
}
