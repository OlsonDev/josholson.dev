using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using NodaTime;

namespace JoshOlson.Database.Migrate;

// Where `seeds:migrate` finds the migrations (and writes `schema.lock.yaml`, beside them) and the seed layers. The
// paths come from the caller, not constants, so the tool can move to another project (ADR 0034, lock file and
// branch-safe migration).
public sealed record MigrateOptions {
  public required string MigrationsDirectory { get; init; }
  public required string SeedsDirectory { get; init; }

  // Where the schema the migrations produce is read from: the template for the newest migration, built from the files
  // in `MigrationsDirectory`. The dev database plays no part, so it can be in any state, or missing.
  public required SchemaTemplate Template { get; init; }
  public required ServerDatabases Databases { get; init; }

  // The time the layers' relative times are measured from while they are moved; a layer's rows come out as offsets from it.
  public required IClock Clock { get; init; }

  // Builds the lock afresh from the database instead of carrying the old one forward: its version, checksums, and
  // accepted checksums are ignored, so a migration rewritten since the last lock no longer counts as a problem. Only
  // for the time before the first deploy, while migrations may still be rewritten (`seeds:relock`).
  public bool Relock { get; init; }

  // Checks the seed layers against the schema the lock is about to describe and returns every problem it finds (empty
  // when they all apply). Called only when there are layers. The real one builds a schema template from the migrations
  // and applies each layer to a clone (`SeedTool.CheckAsync`); without one, a seeds directory with layers is
  // refused.
  public Func<SeedSchema, CancellationToken, Task<List<string>>>? LayerCheck { get; init; }
}
