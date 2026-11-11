using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Seed.Commands;

// The command drops and makes again the dev database, so every test gives the tool a database of its own to do that to.
public sealed class SeedCommandTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _development = $"seed_seedcmd_{Guid.NewGuid():N}";
  private readonly ServerDatabases _databases = new();

  public ValueTask InitializeAsync()
    => ValueTask.CompletedTask;

  public async ValueTask DisposeAsync() {
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(CancellationToken.None);
    await _databases.DropAsync(owner, _development, CancellationToken.None);
  }

  private Action<IServiceCollection> Configure()
    => services => {
      var paths = SeedPaths.ForRepository();
      _ = services.AddSingleton(new SeedToolFactory(paths, _databases, new(paths.Root), SystemClock.Instance, _development));
    };

  [Fact]
  public async Task Execute_Layer_RebuildsTheDevelopmentDatabaseFromIt() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(Configure(), "seed", SeedLayers.Minimal);

    Assert.True(exitCode is 0, output);
    Assert.Contains($"Rebuilt {_development} from {SeedLayers.Minimal}: 3 row(s).", output, StringComparison.Ordinal);
    await using var rebuilt = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_development));
    await rebuilt.OpenAsync(_cancellationToken);
    Assert.Equal(3, await rebuilt.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken));
  }

  [Fact]
  public async Task Execute_UnknownLayer_FailsWithTheMessageAndExitOne() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(Configure(), "seed", "no-such-layer");

    Assert.Equal(1, exitCode);
    Assert.Contains("no-such-layer", output, StringComparison.Ordinal);
    Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NoLayer_IsAMistakeInTheCommandLine() {
    var (exitCode, _) = await SeedAppRunner.RunAsync(Configure(), "seed");

    Assert.Equal(2, exitCode);
  }
}
