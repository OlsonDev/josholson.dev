using JoshOlson.Database;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.IO;
using Xunit;

namespace JoshOlson.Seed.Commands;

public sealed class ListCommandTests : IDisposable {
  private readonly string _directory = TempDirectory.Create("list-command");

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  [Fact]
  public async Task Execute_RealLayers_PrintsEachLayerAndTheCount() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "list");

    Assert.Equal(0, exitCode);
    Assert.Contains(SeedLayers.Minimal, output, StringComparison.Ordinal);
    Assert.Contains(SeedLayers.MoreTags, output, StringComparison.Ordinal);
    Assert.Contains("tag.tag: +3 -0", output, StringComparison.Ordinal);
    Assert.Contains("2 layer(s).", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NoLayersAndNoLock_PrintsZero() {
    var paths = new SeedPaths(_directory);
    _ = Directory.CreateDirectory(paths.Seeds);

    var (exitCode, output) = await SeedAppRunner.RunAsync(paths, "list");

    Assert.Equal(0, exitCode);
    Assert.Contains("0 layer(s).", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_LayersButNoLock_FailsWithTheMessageAndExitOne() {
    var paths = new SeedPaths(_directory);
    _ = Directory.CreateDirectory(paths.Seeds);
    await File.WriteAllTextAsync(Path.Combine(paths.Seeds, "foo.yaml"), "description: Root\n", TestContext.Current.CancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(paths, "list");

    Assert.Equal(1, exitCode);
    Assert.Contains("seeds:migrate", output, StringComparison.Ordinal);
    Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
  }
}
