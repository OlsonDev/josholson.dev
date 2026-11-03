namespace JoshOlson.Database.Locks;

// One way the migrations on disk disagree with the lock. `Message` says what differs and what to do about it.
public sealed record MigrationProblem {
  public required int Version { get; init; }
  public required MigrationProblemKind Kind { get; init; }
  public required string Message { get; init; }
}
