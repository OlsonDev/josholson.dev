using Npgsql;

namespace JoshOlson.Database.Baking;

// A database of a test's own: a `ScratchClone` of the real schema template (built the first time, then reused by every
// test that asks), gone when the test is done. Tests that put rows into one
// can commit, and leave nothing behind in the dev database.
internal sealed class ScratchDatabase : IAsyncDisposable {
  private readonly ScratchClone _clone;

  private ScratchDatabase(ScratchClone clone)
    => _clone = clone;

  public string Name
    => _clone.Name;

  public static async Task<ScratchDatabase> CreateAsync(CancellationToken cancellationToken) {
    var (templateName, _) = await SeedTestServer.RealTemplate.EnsureAsync(cancellationToken);
    return new(await SeedTestServer.Databases.CloneScratchAsync(templateName, "scratch", cancellationToken));
  }

  public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    => await _clone.OpenAsync(cancellationToken);

  public async ValueTask DisposeAsync()
    => await _clone.DisposeAsync();
}
