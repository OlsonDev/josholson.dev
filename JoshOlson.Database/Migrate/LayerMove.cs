using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;

namespace JoshOlson.Database.Migrate;

// What `LayerMover` is asked to do: move the layers of `OldTree`, which were read against the lock `From`, to the schema
// of the lock `To`, through the migrations in `OnDisk`.
public sealed record LayerMove {
  public required SchemaLock From { get; init; }
  public required SchemaLock To { get; init; }
  public required List<MigrationFile> OnDisk { get; init; }
  public required LayerTree OldTree { get; init; }

  // The layers somebody has their own file for, by name, each with its text: kept as written and only checked.
  public Dictionary<string, string> Authored { get; init; } = [];

  // The layers the branch deleted: moved only as parents of the rest, and left out of what comes back.
  public HashSet<string> Dropped { get; init; } = [];
}
