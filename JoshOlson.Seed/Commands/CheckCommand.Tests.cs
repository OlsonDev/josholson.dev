using JoshOlson.Database;
using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Each test checks a copy of the real migrations, lock, and layers, changed the way it needs, so the real ones are never touched.
public sealed class CheckCommandTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedWorkspace _workspace = SeedWorkspace.CopyOfRepository("check-command");

  public void Dispose()
    => _workspace.Dispose();

  private async Task<(int ExitCode, string Output)> RunAsync()
    => await SeedAppRunner.RunAsync(_workspace, "check");

  [Fact]
  public async Task Execute_CurrentSeeds_SaysSoAndExitsZero() {
    var (exitCode, output) = await RunAsync();

    Assert.True(exitCode is 0, output);
    Assert.Contains("The seeds are current", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NoLock_SaysToRunMigrate() {
    File.Delete(_workspace.LockFile);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("Run mise run seeds:migrate", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_MigrationNewerThanTheLock_SaysTheSeedsAreNotMigratedToIt() {
    await _workspace.WriteMigrationAsync("V999__foo.sql", "SELECT 1;\n", _cancellationToken);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("V999__foo.sql is newer than the seeds", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_MigrationChangedSinceTheLock_SaysSo() {
    var migration = Directory.EnumerateFiles(_workspace.Paths.Migrations, "V001__*.sql").Single();
    await File.AppendAllTextAsync(migration, "\n-- foo\n", _cancellationToken);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("has changed since the seeds were migrated against it", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_LayerNotInCanonicalForm_IsListedAndExitsOne() {
    var text = await _workspace.ReadLayerAsync(SeedLayers.Minimal, _cancellationToken);
    var padded = text.Replace("slug: ", "slug:  ", StringComparison.Ordinal);
    await _workspace.WriteLayerAsync(SeedLayers.Minimal, padded, _cancellationToken);

    var (exitCode, output) = await RunAsync();

    Assert.Equal(1, exitCode);
    Assert.Contains("is not in canonical form", output, StringComparison.Ordinal);
  }
}
