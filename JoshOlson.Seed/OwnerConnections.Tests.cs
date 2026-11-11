using System.Data;
using JoshOlson.Database;
using JoshOlson.Shared.Data;
using Xunit;

namespace JoshOlson.Seed;

public sealed class OwnerConnectionsTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task MaintenanceAsync_OpensThePostgresDatabaseOnce() {
    await using var connections = new OwnerConnections();

    var first = await connections.MaintenanceAsync(_cancellationToken);
    var second = await connections.MaintenanceAsync(_cancellationToken);

    Assert.Same(first, second);
    Assert.Equal("postgres", await first.QuerySingleAsync<string>("SELECT current_database();", _cancellationToken));
  }

  [Fact]
  public async Task DevelopmentAsync_OpensTheDevDatabaseOnce() {
    await using var connections = new OwnerConnections();

    var first = await connections.DevelopmentAsync(_cancellationToken);
    var second = await connections.DevelopmentAsync(_cancellationToken);

    Assert.Same(first, second);
    Assert.Equal(
      DatabaseEnvironment.DevelopmentDatabase(),
      await first.QuerySingleAsync<string>("SELECT current_database();", _cancellationToken));
  }

  [Fact]
  public async Task DisposeAsync_ClosesWhatWasOpened() {
    var connections = new OwnerConnections();
    var maintenance = await connections.MaintenanceAsync(_cancellationToken);
    var development = await connections.DevelopmentAsync(_cancellationToken);

    await connections.DisposeAsync();

    Assert.Equal(ConnectionState.Closed, maintenance.State);
    Assert.Equal(ConnectionState.Closed, development.State);
  }

  [Fact]
  public async Task DisposeAsync_NothingOpened_DoesNothing()
    => await new OwnerConnections().DisposeAsync();

  [Fact]
  public async Task Dispose_ClosesWhatWasOpened() {
    var connections = new OwnerConnections();
    var maintenance = await connections.MaintenanceAsync(_cancellationToken);

    // The container disposes a singleton that has both, synchronously, so that is the path under test.
#pragma warning disable MA0042
    connections.Dispose();
#pragma warning restore MA0042

    Assert.Equal(ConnectionState.Closed, maintenance.State);
  }
}
