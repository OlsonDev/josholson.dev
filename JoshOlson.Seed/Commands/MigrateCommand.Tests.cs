using JoshOlson.Database;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Reads the schema from the template for a copy of the repository's migrations, which is the real template (current after
// any `mise run seeds:bake`) while the files are the same. Each test copies the real migrations and seeds into a directory
// of its own, so the real lock is never touched.
public sealed class MigrateCommandTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedWorkspace _workspace = SeedWorkspace.CopyOfRepository("migrate-command", withLock: false, withLayers: false);

  public void Dispose()
    => _workspace.Dispose();

  private async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    => await SeedAppRunner.RunAsync(_workspace, arguments);

  [Fact]
  public async Task Execute_NoLock_WritesOneAndSaysSo() {
    var (exitCode, output) = await RunAsync("migrate");

    Assert.True(exitCode is 0, output);
    Assert.Contains("Wrote", output, StringComparison.Ordinal);
    Assert.Contains("no lock to version", output, StringComparison.Ordinal);
    Assert.True(File.Exists(_workspace.LockFile));
  }

  [Fact]
  public async Task Execute_SecondRun_FindsTheLockAlreadyRight() {
    _ = await RunAsync("migrate");
    var text = await _workspace.ReadLockAsync(_cancellationToken);

    var (exitCode, output) = await RunAsync("migrate");

    Assert.True(exitCode is 0, output);
    Assert.Contains("is already at version", output, StringComparison.Ordinal);
    Assert.Equal(text, await _workspace.ReadLockAsync(_cancellationToken));
  }

  [Fact]
  public async Task Execute_Relock_StartsTheLockOver() {
    _ = await RunAsync("migrate");

    var (exitCode, output) = await RunAsync("migrate", "--relock");

    Assert.True(exitCode is 0, output);
    Assert.True(File.Exists(_workspace.LockFile));
  }

  [Fact]
  public async Task Execute_LayersThatApply_WritesTheLock() {
    _workspace.CopyRepositoryLayers();

    var (exitCode, output) = await RunAsync("migrate");

    Assert.True(exitCode is 0, output);
    Assert.True(File.Exists(_workspace.LockFile));
  }

  [Fact]
  public async Task Execute_LayerThatCannotBeRead_FailsWritesNoLockAndSaysWhy() {
    await _workspace.WriteLayerAsync("foo", "description: Foo\nextends: nope\n", _cancellationToken);

    var (exitCode, output) = await RunAsync("migrate");

    Assert.Equal(1, exitCode);
    Assert.Contains("nope", output, StringComparison.Ordinal);
    Assert.False(File.Exists(_workspace.LockFile));
  }
}
