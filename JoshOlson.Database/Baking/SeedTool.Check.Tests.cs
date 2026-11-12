using JoshOlson.Database.Layers;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Each test works on a copy of the real seeds, so a layer can be broken without touching the real one.
public sealed class SeedToolCheckTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedRepositoryCopy _copy = new("seed-check");
  private NpgsqlConnection _owner = null!;

  public async ValueTask InitializeAsync() {
    _owner = new(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await _owner.OpenAsync(_cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    await _owner.DisposeAsync();
    _copy.Dispose();
  }

  private async Task<List<string>> CheckAsync() {
    var tool = await SeedTool.OpenAsync(
      _copy.Paths,
      new ServerDatabases(),
      new ComposeTools(SeedPaths.ForRepository().Root),
      SystemClock.Instance,
      _cancellationToken);
    return await tool.CheckFilesAsync(_owner, _copy.Paths.Seeds, _cancellationToken);
  }

  private async Task ReplaceInLayerAsync(string layer, string from, string to) {
    var text = await File.ReadAllTextAsync(_copy.Layer(layer), _cancellationToken);
    Assert.Contains(from, text, StringComparison.Ordinal);
    await File.WriteAllTextAsync(_copy.Layer(layer), text.Replace(from, to, StringComparison.Ordinal), _cancellationToken);
  }

  [Fact]
  public async Task CheckFilesAsync_TheRealLayers_HaveNoProblems()
    => Assert.Empty(await CheckAsync());

  [Fact]
  public async Task CheckFilesAsync_LayerNotInCanonicalForm_SaysWhereAndSkipsTheLayersBelowIt() {
    await ReplaceInLayerAsync(SeedLayers.Minimal, "slug: ", "slug:  ");

    var problems = await CheckAsync();

    var problem = Assert.Single(problems);
    Assert.StartsWith("minimal: ", problem, StringComparison.Ordinal);
    Assert.Contains("is not in canonical form (line ", problem, StringComparison.Ordinal);
    Assert.Contains("slug:  ", problem, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CheckFilesAsync_LayerThatDoesNotBuild_NamesItAndWhy() {
    // The merge accepts two tags with the same slug; the unique constraint only shows when Postgres sees the rows.
    await ReplaceInLayerAsync(SeedLayers.MoreTags, "slug: library,", "slug: framework,");

    var problems = await CheckAsync();

    Assert.Contains(problems, problem => problem.StartsWith("more-tags: ", StringComparison.Ordinal));
  }
}
