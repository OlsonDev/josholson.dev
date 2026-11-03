namespace JoshOlson.Database.Locks;

// The migrations on disk against the lock. `Problems` is empty when the history the seeds were migrated against is the
// history on disk. `Pending` are the migrations after the locked version: what `seeds:migrate` applies next.
public sealed record LockCheck {
  public required List<MigrationProblem> Problems { get; init; }
  public required List<MigrationFile> Pending { get; init; }

  public bool IsConsistent
    => Problems.Count is 0;
}
