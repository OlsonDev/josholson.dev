using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Seed.Commands;

public sealed class TemplateCommandTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task Execute_Layer_PrintsTheNameOfATemplateThatExistsAndTheSameNameAgain() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "template", FixtureSeeds.Child);
    var (againCode, again) = await SeedAppRunner.RunAsync(workspace, "template", FixtureSeeds.Child);

    var name = output.Trim();
    Assert.True(exitCode is 0, output);
    Assert.True(SeedDatabaseNames.IsTemplate(name), name);
    Assert.True(await SeedTestServer.Databases.ExistsAsync(name, _cancellationToken));
    Assert.Equal(0, againCode);
    Assert.Equal(name, again.Trim());
  }

  [Fact]
  public async Task Execute_LayersWithDifferentRows_PrintDifferentNames() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);

    var (rootCode, root) = await SeedAppRunner.RunAsync(workspace, "template", FixtureSeeds.Root);
    var (childCode, child) = await SeedAppRunner.RunAsync(workspace, "template", FixtureSeeds.Child);

    Assert.True(rootCode is 0 && childCode is 0, root + child);
    Assert.NotEqual(root.Trim(), child.Trim());
  }

  [Fact]
  public async Task Execute_Blank_PrintsTheSchemaTemplate() {
    var (exitCode, output) = await SeedAppRunner.RunAsync("template", LayerName.Blank);

    Assert.True(exitCode is 0, output);
    Assert.StartsWith("seed_schema_", output.Trim(), StringComparison.Ordinal);
    Assert.True(await SeedTestServer.Databases.ExistsAsync(output.Trim(), _cancellationToken));
  }

  [Fact]
  public async Task Execute_LayerThatDoesNotExist_FailsWithExitOne() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "template", "nosuchlayer");

    Assert.Equal(1, exitCode);
    Assert.Contains("nosuchlayer", output, StringComparison.Ordinal);
  }
}
