namespace JoshOlson.Database.Migrate;

// A base a test has made up, so the logic that reads one is tried without git: it returns what it was given, and says
// what it was asked for.
internal sealed class FakeSnapshotSource : ISnapshotSource {
  private readonly SnapshotRead _read;

  public FakeSnapshotSource(SnapshotRead read)
    => _read = read;

  public int Reads { get; private set; }

  public Task<SnapshotRead> ReadAsync(string? baseRef, bool fetch, CancellationToken cancellationToken) {
    Reads++;
    return Task.FromResult(_read);
  }
}
