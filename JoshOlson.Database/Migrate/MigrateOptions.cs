namespace JoshOlson.Database.Migrate;

// Where `seeds:migrate` finds the migrations (and writes `schema.lock.yaml`, beside them) and the seed layers. The
// paths come from the caller, not constants, so the tool can move to another project (ADR 0034, lock file and
// branch-safe migration).
public sealed record MigrateOptions {
  public required string MigrationsDirectory { get; init; }
  public required string SeedsDirectory { get; init; }
}
