using Npgsql;

namespace JoshOlson.Database.Baking;

// A clone of a template that lives for one piece of work: a template refuses connections, so whatever reads one or tries
// rows against one does it in a clone (`ServerDatabases.CloneScratchAsync`) and disposes it, which drops the database
// even when the work was canceled. The name says what the clone is for (`SeedDatabaseNames.Scratch`).
public sealed class ScratchClone : IAsyncDisposable {
  private readonly ServerDatabases _databases;

  internal ScratchClone(ServerDatabases databases, string name) {
    _databases = databases;
    Name = name;
  }

  public string Name { get; }

  // An owner connection to the clone, which the caller disposes before the clone.
  public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    => await DatabaseEnvironment.OpenOwnerAsync(Name, cancellationToken);

  public async ValueTask DisposeAsync()
    => await _databases.DropAsync(Name, CancellationToken.None);
}
