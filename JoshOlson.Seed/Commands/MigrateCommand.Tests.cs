using JoshOlson.Database;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Each test runs `migrate` in a workspace of its own, so the real lock is never touched. The tests that are not about
// the real schema use one small migration with fixed text, whose schema template is built the first time and stays on
// the server (a template's name is a hash of the files).
public sealed class MigrateCommandTests {
  private const string _FooFile = "V001__foo.sql";

  private const string _Foo = """
    CREATE SCHEMA foo;

    CREATE TABLE foo.bar (
        bar_id  INT   NOT NULL GENERATED ALWAYS AS IDENTITY
      , name    TEXT  NOT NULL

      , CONSTRAINT pk_bar PRIMARY KEY (bar_id)
    );

    """;

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  private async Task<SeedWorkspace> FooWorkspaceAsync() {
    var workspace = SeedWorkspace.Empty("migrate-command");
    try {
      await workspace.WriteMigrationAsync(_FooFile, _Foo, _cancellationToken);
    } catch {
      workspace.Dispose();
      throw;
    }
    return workspace;
  }

  [Fact]
  public async Task Execute_NoLockThenAgainThenRelock_WritesItFindsItRightAndStartsItOver() {
    using var workspace = await FooWorkspaceAsync();

    var (firstCode, first) = await SeedAppRunner.RunAsync(workspace, "migrate");
    var written = await workspace.ReadLockAsync(_cancellationToken);
    var (secondCode, second) = await SeedAppRunner.RunAsync(workspace, "migrate");
    var afterSecond = await workspace.ReadLockAsync(_cancellationToken);
    var (relockCode, relock) = await SeedAppRunner.RunAsync(workspace, "migrate", "--relock");

    Assert.True(firstCode is 0, first);
    Assert.Contains("Wrote", first, StringComparison.Ordinal);
    Assert.Contains("no lock to version 1", first, StringComparison.Ordinal);
    Assert.True(secondCode is 0, second);
    Assert.Contains("is already at version 1", second, StringComparison.Ordinal);
    Assert.Equal(written, afterSecond);
    Assert.True(relockCode is 0, relock);
    // Nothing was rewritten, so starting over arrives at the same lock.
    Assert.Contains("is already at version 1", relock, StringComparison.Ordinal);
    Assert.Equal(written, await workspace.ReadLockAsync(_cancellationToken));
  }

  // Over the real migrations, which the fixture layers are written against, with no lock yet.
  [Fact]
  public async Task Execute_LayersThatApply_WritesTheLock() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    File.Delete(workspace.LockFile);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "migrate");

    Assert.True(exitCode is 0, output);
    Assert.True(File.Exists(workspace.LockFile));
  }

  [Fact]
  public async Task Execute_LayerThatCannotBeRead_FailsWritesNoLockAndSaysWhy() {
    using var workspace = await FooWorkspaceAsync();
    await workspace.WriteLayerAsync("foo", "description: Foo\nextends: nope\n", _cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "migrate");

    Assert.Equal(1, exitCode);
    Assert.Contains("nope", output, StringComparison.Ordinal);
    Assert.False(File.Exists(workspace.LockFile));
  }
}
