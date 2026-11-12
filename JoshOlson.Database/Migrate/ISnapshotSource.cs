namespace JoshOlson.Database.Migrate;

// Where `seeds:migrate` finds the base: the lock and layers of the commit the branch is measured from. The real one reads
// git (`GitSnapshotSource`); a test hands it a snapshot of its own. With no `baseRef` it is the remote's main, fetched first
// when `fetch` says so; an explicit one is a commit or ref as it is.
public interface ISnapshotSource {
  public Task<SnapshotRead> ReadAsync(string? baseRef, bool fetch, CancellationToken cancellationToken);
}
