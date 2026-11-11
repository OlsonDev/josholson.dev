using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Bakes the real templates, as the tool does, so the first run in a clean checkout builds them (it takes a while) and
// every later one finds them current.
public sealed class BakeCommandTests {
  [Fact]
  public async Task Execute_RealLayers_ListsTheSchemaTemplateAndEachLayerAndThenFindsThemCurrent() {
    var (firstExitCode, firstOutput) = await SeedAppRunner.RunAsync(paths: null, "bake");

    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "bake");

    Assert.True(firstExitCode is 0, firstOutput);
    Assert.True(exitCode is 0, output);
    Assert.Contains("(schema)", output, StringComparison.Ordinal);
    Assert.Contains(SeedDatabaseNames.Prefix + "schema_", output, StringComparison.Ordinal);
    Assert.Contains(SeedLayers.Minimal, output, StringComparison.Ordinal);
    Assert.Contains(SeedLayers.MoreTags, output, StringComparison.Ordinal);
    Assert.Contains("current", output, StringComparison.Ordinal);
    Assert.DoesNotContain("built", output, StringComparison.Ordinal);
  }
}
