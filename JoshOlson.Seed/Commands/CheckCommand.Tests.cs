using JoshOlson.Database;
using JoshOlson.Database.Layers;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Each test checks a copy of the real migrations, lock, and layers, changed the way it needs, so the real ones are never touched.
public sealed class CheckCommandTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedRepositoryCopy _copy = new("check-command");
  private readonly SeedToolFactory _factory;

  public CheckCommandTests()
    => _factory = new(_copy.Paths, new(), new(SeedPaths.ForRepository().Root), SystemClock.Instance, DatabaseEnvironment.DevelopmentDatabase());

  public void Dispose()
    => _copy.Dispose();

  private async Task<(int ExitCode, string Output)> RunAsync()
    => await SeedAppRunner.RunAsync(
      services => {
        _ = services.AddSingleton(_copy.Paths);
        _ = services.AddSingleton(_factory);
      },
      ["check"]);

  [Fact]
  public async Task Execute_CurrentSeeds_SaysSoAndExitsZero() {
    var (exitCode, output) = await RunAsync();

    Assert.True(exitCode is 0, output);
    Assert.Contains("The seeds are current", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NoLock_SaysToRunMigrate() {
    File.Delete(_copy.Paths.LockFile);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("Run mise run seeds:migrate", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_MigrationNewerThanTheLock_SaysTheSeedsAreNotMigratedToIt() {
    await File.WriteAllTextAsync(Path.Combine(_copy.Paths.Migrations, "V999__foo.sql"), "SELECT 1;\n", _cancellationToken);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("V999__foo.sql is newer than the seeds", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_MigrationChangedSinceTheLock_SaysSo() {
    var migration = Directory.EnumerateFiles(_copy.Paths.Migrations, "V001__*.sql").Single();
    await File.AppendAllTextAsync(migration, "\n-- foo\n", _cancellationToken);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("has changed since the seeds were migrated against it", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_LayerNotInCanonicalForm_IsListedAndExitsOne() {
    var path = _copy.Layer(SeedLayers.Minimal);
    await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path, _cancellationToken)).Replace("slug: ", "slug:  ", StringComparison.Ordinal), _cancellationToken);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("is not in canonical form", output, StringComparison.Ordinal);
  }
}
