using JoshOlson.Database;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.IO;
using NodaTime;
using Xunit;

namespace JoshOlson.Seed;

public sealed class SeedToolFactoryTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = TempDirectory.Create("seed-tool-factory");

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private static SeedToolFactory Factory(SeedPaths paths)
    => new(paths, new(), new(paths.Root), SystemClock.Instance);

  [Fact]
  public async Task OpenAsync_RepositoryPaths_LoadsTheRealLayers() {
    var tool = await Factory(SeedPaths.ForRepository()).OpenAsync(_cancellationToken);

    Assert.Contains(SeedLayers.Minimal, tool.Tree.InBakeOrder().Select(layer => layer.Name));
    Assert.Contains(SeedLayers.MoreTags, tool.Tree.InBakeOrder().Select(layer => layer.Name));
  }

  [Fact]
  public async Task OpenAsync_NoLock_SaysToRunSeedsMigrate() {
    var factory = Factory(new SeedPaths(_directory));

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => factory.OpenAsync(_cancellationToken));

    Assert.Contains("seeds:migrate", exception.Message, StringComparison.Ordinal);
  }
}
