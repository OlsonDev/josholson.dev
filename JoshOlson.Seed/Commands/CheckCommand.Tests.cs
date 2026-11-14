using JoshOlson.Database;
using Xunit;

namespace JoshOlson.Seed.Commands;

// The real repository is checked where it is, since `check` writes nothing. A test that breaks something does it in a
// workspace of its own with the fixture layers, so the real files are never touched.
public sealed class CheckCommandTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task Execute_CurrentSeeds_SaysSoAndExitsZero() {
    var (exitCode, output) = await SeedAppRunner.RunAsync("check");

    Assert.True(exitCode is 0, output);
    Assert.Contains("The seeds are current", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_NoLock_SaysToRunMigrate() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    File.Delete(workspace.LockFile);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "check");

    Assert.Equal(1, exitCode);
    Assert.Contains("Run mise run seeds:migrate", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_MigrationNewerThanTheLock_SaysTheSeedsAreNotMigratedToIt() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    await workspace.WriteMigrationAsync("V999__foo.sql", "SELECT 1;\n", _cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "check");

    Assert.Equal(1, exitCode);
    Assert.Contains("V999__foo.sql is newer than the seeds", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_MigrationChangedSinceTheLock_SaysSo() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    var migration = Directory.EnumerateFiles(workspace.Paths.Migrations, "V001__*.sql").Single();
    await File.AppendAllTextAsync(migration, "\n-- foo\n", _cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "check");

    Assert.Equal(1, exitCode);
    Assert.Contains("has changed since the seeds were migrated against it", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_LayerNotInCanonicalForm_IsListedAndExitsOne() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    var padded = FixtureSeeds.RootLayer.Replace("slug: ", "slug:  ", StringComparison.Ordinal);
    await workspace.WriteLayerAsync(FixtureSeeds.Root, padded, _cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "check");

    Assert.Equal(1, exitCode);
    Assert.Contains("is not in canonical form", output, StringComparison.Ordinal);
  }
}
