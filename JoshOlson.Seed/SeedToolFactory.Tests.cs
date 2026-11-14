using JoshOlson.Database;
using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Seed;

public sealed class SeedToolFactoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  private static SeedToolFactory Factory(SeedPaths paths)
    => SeedAppRunner.FactoryFor(paths, "seed_foo");

  [Fact]
  public async Task OpenAsync_RepositoryPaths_LoadsTheRealLayersForTheDatabaseTheFactoryWasGiven() {
    var tool = await Factory(SeedTestServer.Paths).OpenAsync(_cancellationToken);

    Assert.Contains(SeedLayers.Minimal, tool.Tree.InBakeOrder().Select(layer => layer.Name));
    Assert.Contains(SeedLayers.MoreTags, tool.Tree.InBakeOrder().Select(layer => layer.Name));
    Assert.Equal("seed_foo", tool.Development);
  }

  [Fact]
  public async Task OpenAsync_NoLock_SaysToRunSeedsMigrate() {
    using var workspace = SeedWorkspace.Empty("seed-tool-factory");
    var factory = Factory(workspace.Paths);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => factory.OpenAsync(_cancellationToken));

    Assert.Contains("seeds:migrate", exception.Message, StringComparison.Ordinal);
  }
}
