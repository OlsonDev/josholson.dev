namespace JoshOlson.Database.Migrate;

// What `seeds:migrate` did: the lock's path, the version it said before (null if there was no lock) and says now, and
// whether the file changed. A run that finds the lock already right is not a change.
public sealed record MigrateResult {
  public required string Path { get; init; }
  public required int? PreviousVersion { get; init; }
  public required int Version { get; init; }
  public required bool Changed { get; init; }

  // The layers `seeds:migrate` moved to the newest schema, and whether each file was rewritten. Empty when there was nothing
  // to move: no layers, or a lock that was not behind the migrations.
  public List<(string Layer, bool Rewritten)> Layers { get; init; } = [];

  // The base the lock and layers were derived from (a description of the commit), when they were; null when the working
  // tree was the input.
  public string? Base { get; init; }

  // The layers that were somebody's own files and so were kept as they are and checked, not regenerated.
  public List<string> Kept { get; init; } = [];

  // What reading the base had to say: a fetch that failed, a base that was not there.
  public List<string> Warnings { get; init; } = [];
}
