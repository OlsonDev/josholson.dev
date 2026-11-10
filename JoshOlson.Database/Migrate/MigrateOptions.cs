namespace JoshOlson.Database.Migrate;

// Where `seeds:migrate` finds the migrations (and writes `schema.lock.yaml`, beside them) and the seed layers. The
// paths come from the caller, not constants, so the tool can move to another project (ADR 0034, lock file and
// branch-safe migration).
public sealed record MigrateOptions {
  public required string MigrationsDirectory { get; init; }
  public required string SeedsDirectory { get; init; }

  // Builds the lock afresh from the database instead of carrying the old one forward: its version, checksums, and
  // accepted checksums are ignored, so a migration rewritten since the last lock no longer counts as a problem. Only
  // for the time before the first deploy, while migrations may still be rewritten (`seeds:relock`).
  public bool Relock { get; init; }
}
