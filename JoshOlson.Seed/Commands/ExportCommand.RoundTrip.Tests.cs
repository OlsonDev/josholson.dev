using JoshOlson.Database;
using Xunit;

namespace JoshOlson.Seed.Commands;

// What `export` rests on: a database built from a layer exports as that layer's own file. In a class of its own so it
// runs beside `ExportCommandTests`.
public sealed class ExportCommandRoundTripTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ExportCommandHarness _harness = new();

  public async ValueTask InitializeAsync()
    => await _harness.InitializeAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _harness.DisposeAsync();

  // A layer's text with its description line replaced, which is all a copy exported from its database should differ by.
  private static string WithDescription(string yaml, string description)
    => string.Join('\n', [$"description: {description}", .. yaml.Split('\n').Skip(1)]);

  [Fact]
  public async Task Execute_DatabasesBuiltFromAChildAndFromARoot_ReproduceTheLayersFilesByteForByte() {
    var child = await _harness.DatabaseFromAsync(FixtureSeeds.Child);
    // Recorded as built from the root, the layer the child extends, so the copy is the difference from its template.
    await _harness.RunSqlAsync(child, $"UPDATE seed.hydration SET layer = '{FixtureSeeds.Root}';");
    var root = await _harness.DatabaseFromAsync(FixtureSeeds.Root);
    // Recorded as built from no layer, so the copy is a root: the difference from the schema template.
    await _harness.RunSqlAsync(root, "UPDATE seed.hydration SET layer = NULL;");

    var (childCode, childOutput) = await _harness.ExportAsync(child, "--name", "child-copy", "--description", "Copy");
    var (rootCode, rootOutput) = await _harness.ExportAsync(root, "--name", "root-copy", "--description", "Copy");

    Assert.True(childCode is 0, childOutput);
    Assert.Equal(WithDescription(FixtureSeeds.ChildLayer, "Copy"), await _harness.ReadLayerAsync("child-copy"));
    Assert.True(rootCode is 0, rootOutput);
    Assert.Equal(WithDescription(FixtureSeeds.RootLayer, "Copy"), await _harness.ReadLayerAsync("root-copy"));
  }
}
