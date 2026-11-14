using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// The real layers are checked where they are, since a check writes nothing. A test that breaks a layer does it in a
// workspace of its own with the fixture layers.
public sealed class SeedToolCheckTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private NpgsqlConnection _owner = null!;

  public async ValueTask InitializeAsync()
    => _owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _owner.DisposeAsync();

  // The problems `CheckFilesAsync` finds in the fixture layers once `from` in one of them reads `to`.
  private async Task<List<string>> CheckWithAsync(string layer, string from, string to) {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    var text = await workspace.ReadLayerAsync(layer, _cancellationToken);
    Assert.Contains(from, text, StringComparison.Ordinal);
    await workspace.WriteLayerAsync(layer, text.Replace(from, to, StringComparison.Ordinal), _cancellationToken);
    var tool = await workspace.OpenToolAsync(_cancellationToken);
    return await tool.CheckFilesAsync(_owner, workspace.Paths.Seeds, _cancellationToken);
  }

  [Fact]
  public async Task CheckFilesAsync_TheRealLayers_HaveNoProblems() {
    var tool = await SeedTestServer.RealTool;

    Assert.Empty(await tool.CheckFilesAsync(_owner, SeedTestServer.Paths.Seeds, _cancellationToken));
  }

  [Fact]
  public async Task CheckFilesAsync_LayerNotInCanonicalForm_SaysWhereAndSkipsTheLayersBelowIt() {
    var problems = await CheckWithAsync(FixtureSeeds.Root, "slug: ", "slug:  ");

    var problem = Assert.Single(problems);
    Assert.StartsWith($"{FixtureSeeds.Root}: ", problem, StringComparison.Ordinal);
    Assert.Contains("is not in canonical form (line ", problem, StringComparison.Ordinal);
    Assert.Contains("slug:  ", problem, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CheckFilesAsync_LayerThatDoesNotBuild_NamesItAndWhy() {
    // The merge accepts two tags with the same slug; the unique constraint only shows when Postgres sees the rows.
    var problems = await CheckWithAsync(FixtureSeeds.Child, "slug: waldo,", "slug: xyzzy,");

    Assert.Contains(problems, problem => problem.StartsWith($"{FixtureSeeds.Child}: ", StringComparison.Ordinal));
  }
}
