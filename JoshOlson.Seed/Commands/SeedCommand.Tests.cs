using JoshOlson.Database;
using JoshOlson.Shared.Data;
using JoshOlson.Shared.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JoshOlson.Seed.Commands;

// The command drops and makes again the dev database, so every test gives the tool a database of its own to do that to.
public sealed class SeedCommandTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _development = $"seed_seedcmd_{Guid.NewGuid():N}";

  public ValueTask InitializeAsync()
    => ValueTask.CompletedTask;

  public async ValueTask DisposeAsync() {
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(CancellationToken.None);
    await SeedTestServer.Databases.DropAsync(owner, _development, CancellationToken.None);
  }

  private Action<IServiceCollection> Configure(SeedPaths paths)
    => services => services.AddSingleton(SeedAppRunner.FactoryFor(paths, _development));

  [Fact]
  public async Task Execute_Layer_RebuildsTheDevelopmentDatabaseFromIt() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    var rows = FixtureSeeds.RootRows;

    var (exitCode, output) = await SeedAppRunner.RunAsync(Configure(workspace.Paths), "seed", FixtureSeeds.Root);

    Assert.True(exitCode is 0, output);
    Assert.Contains(
      $"Rebuilt {_development} from {FixtureSeeds.Root}: {rows.ToInvariantString()} row(s).",
      output,
      StringComparison.Ordinal);
    await using var rebuilt = await DatabaseEnvironment.OpenOwnerAsync(_development, _cancellationToken);
    Assert.Equal(rows, await rebuilt.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken));
  }

  [Fact]
  public async Task Execute_UnknownLayer_FailsWithTheMessageAndExitOne() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(Configure(SeedTestServer.Paths), "seed", "no-such-layer");

    Assert.Equal(1, exitCode);
    Assert.Contains("no-such-layer", output, StringComparison.Ordinal);
    Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NoLayer_IsAMistakeInTheCommandLine() {
    var (exitCode, _) = await SeedAppRunner.RunAsync(Configure(SeedTestServer.Paths), "seed");

    Assert.Equal(2, exitCode);
  }
}
