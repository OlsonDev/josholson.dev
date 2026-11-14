using JoshOlson.Database;
using JoshOlson.Shared.Extensions;
using Xunit;

namespace JoshOlson.Seed.Commands;

public sealed class ListCommandTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task Execute_Layers_PrintsEachLayerWithItsCountsAndTheNumberOfLayers() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "list");

    Assert.Equal(0, exitCode);
    foreach (var layer in FixtureSeeds.Tree.InBakeOrder()) {
      Assert.Contains($"{layer.Name}: {layer.Description}", output, StringComparison.Ordinal);
      foreach (var table in layer.Tables) {
        var inserts = table.Inserts.Count.ToInvariantString();
        var updates = table.Updates.Count.ToInvariantString();
        var deletes = table.Deletes.Count.ToInvariantString();
        Assert.Contains($"{table.Table}: +{inserts} ~{updates} -{deletes}", output, StringComparison.Ordinal);
      }
    }
    Assert.Contains($"{FixtureSeeds.Tree.Count.ToInvariantString()} layer(s).", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NoLayersAndNoLock_PrintsZero() {
    using var workspace = SeedWorkspace.Empty("list-command");

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "list");

    Assert.Equal(0, exitCode);
    Assert.Contains("0 layer(s).", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_LayersButNoLock_FailsWithTheMessageAndExitOne() {
    using var workspace = SeedWorkspace.Empty("list-command");
    await workspace.WriteLayerAsync("foo", "description: Root\n", _cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "list");

    Assert.Equal(1, exitCode);
    Assert.Contains("seeds:migrate", output, StringComparison.Ordinal);
    Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
  }
}
