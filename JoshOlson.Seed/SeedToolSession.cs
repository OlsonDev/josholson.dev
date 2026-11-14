using JoshOlson.Database.Baking;
using Npgsql;

namespace JoshOlson.Seed;

// What most commands start with: the tool, opened from the files as they are now (`SeedToolFactory`), and the owner
// connection to the maintenance database that every one of its operations takes (`OwnerConnections`), so a command
// holds one thing and asks once.
public sealed class SeedToolSession {
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;

  public SeedToolSession(SeedToolFactory factory, OwnerConnections connections) {
    _factory = factory;
    _connections = connections;
  }

  // The lock has to exist, as for `SeedToolFactory.OpenAsync`. The connection is the run's, so the caller does not
  // dispose it.
  public async Task<(SeedTool Tool, NpgsqlConnection Owner)> OpenAsync(CancellationToken cancellationToken)
    => (await _factory.OpenAsync(cancellationToken), await _connections.MaintenanceAsync(cancellationToken));
}
