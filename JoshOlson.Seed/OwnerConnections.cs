using JoshOlson.Database;
using Npgsql;

namespace JoshOlson.Seed;

// The owner connections a command asks for, each opened the first time and kept until the run ends: a run is one command,
// so the process is the scope (the API's connection lives for a request instead). The maintenance database is where the
// commands that drop, rename, or clone a database connect, because Postgres cannot do any of those to a database someone
// is connected to; the dev database is for the ones that read its catalog.
public sealed class OwnerConnections : IDisposable, IAsyncDisposable {
  private NpgsqlConnection? _maintenance;
  private NpgsqlConnection? _development;

  public async Task<NpgsqlConnection> MaintenanceAsync(CancellationToken cancellationToken)
    => _maintenance ??= await OpenAsync(DatabaseEnvironment.OwnerConnectionString("postgres"), cancellationToken);

  public async Task<NpgsqlConnection> DevelopmentAsync(CancellationToken cancellationToken)
    => _development ??= await OpenAsync(DatabaseEnvironment.OwnerConnectionString(), cancellationToken);

  public void Dispose() {
    _maintenance?.Dispose();
    _development?.Dispose();
  }

  public async ValueTask DisposeAsync() {
    if (_maintenance is not null) {
      await _maintenance.DisposeAsync();
    }
    if (_development is not null) {
      await _development.DisposeAsync();
    }
  }

  private static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken) {
    var connection = new NpgsqlConnection(connectionString);
    try {
      await connection.OpenAsync(cancellationToken);
    } catch {
      await connection.DisposeAsync();
      throw;
    }
    return connection;
  }
}
