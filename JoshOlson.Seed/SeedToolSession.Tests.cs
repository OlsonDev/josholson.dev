using JoshOlson.Database;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using Xunit;

namespace JoshOlson.Seed;

public sealed class SeedToolSessionTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task OpenAsync_RepositoryPaths_IsTheFactorysToolAndTheRunsMaintenanceConnection() {
    await using var connections = new OwnerConnections();
    var session = new SeedToolSession(SeedAppRunner.FactoryFor(SeedTestServer.Paths, "seed_foo"), connections);

    var (tool, owner) = await session.OpenAsync(_cancellationToken);

    Assert.Contains(SeedLayers.Minimal, tool.Tree.InBakeOrder().Select(layer => layer.Name));
    Assert.Equal("seed_foo", tool.Development);
    Assert.Same(await connections.MaintenanceAsync(_cancellationToken), owner);
    var database = await owner.QuerySingleAsync<string>("SELECT current_database() AS name;", _cancellationToken);
    Assert.Equal("postgres", database);
  }

  [Fact]
  public async Task OpenAsync_NoLock_SaysToRunSeedsMigrate() {
    using var workspace = SeedWorkspace.Empty("seed-tool-session");
    await using var connections = new OwnerConnections();
    var session = new SeedToolSession(SeedAppRunner.FactoryFor(workspace.Paths), connections);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => session.OpenAsync(_cancellationToken));

    Assert.Contains("seeds:migrate", exception.Message, StringComparison.Ordinal);
  }
}
