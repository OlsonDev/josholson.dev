using JoshOlson.Database;
using JoshOlson.Shared.IO;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Reads the schema from the template for a copy of the repository's migrations, which is the real template (current after
// any `mise run seeds:bake`) while the files are the same. Each test copies the real migrations and seeds into a directory
// of its own, so the real lock is never touched.
public sealed class MigrateCommandTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = TempDirectory.Create("migrate-command");
  private readonly SeedPaths _paths;

  public MigrateCommandTests() {
    _paths = new(_directory);
    _ = Directory.CreateDirectory(_paths.Migrations);
    foreach (var file in Directory.EnumerateFiles(SeedPaths.ForRepository().Migrations, "*.sql")) {
      File.Copy(file, Path.Combine(_paths.Migrations, Path.GetFileName(file)));
    }
    _ = Directory.CreateDirectory(_paths.Seeds);
    // The template's name is a hash of these files, so the copies make a layer check find the real templates current.
    foreach (var script in new[] { _paths.ProvisioningScript, _paths.SeedSchemaScript }) {
      _ = Directory.CreateDirectory(Path.GetDirectoryName(script)!);
    }
    File.Copy(SeedPaths.ForRepository().ProvisioningScript, _paths.ProvisioningScript);
    File.Copy(SeedPaths.ForRepository().SeedSchemaScript, _paths.SeedSchemaScript);
  }

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private void CopyRealLayers() {
    foreach (var file in Directory.EnumerateFiles(SeedPaths.ForRepository().Seeds, "*.yaml")) {
      File.Copy(file, Path.Combine(_paths.Seeds, Path.GetFileName(file)));
    }
  }

  // The paths are the copy's, except that a layer check builds templates by running containers, which needs the real
  // repository's compose files: only `ComposeTools` keeps the real root.
  private async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    => await SeedAppRunner.RunAsync(
      services => {
        _ = services.AddSingleton(_paths);
        _ = services.AddSingleton(new SeedToolFactory(
          _paths,
          new(),
          new(SeedPaths.ForRepository().Root),
          SystemClock.Instance,
          DatabaseEnvironment.DevelopmentDatabase()));
      },
      arguments);

  [Fact]
  public async Task Execute_NoLock_WritesOneAndSaysSo() {
    var (exitCode, output) = await RunAsync("migrate");

    Assert.True(exitCode is 0, output);
    Assert.Contains("Wrote", output, StringComparison.Ordinal);
    Assert.Contains("no lock to version", output, StringComparison.Ordinal);
    Assert.True(File.Exists(_paths.LockFile));
  }

  [Fact]
  public async Task Execute_SecondRun_FindsTheLockAlreadyRight() {
    _ = await RunAsync("migrate");
    var text = await File.ReadAllTextAsync(_paths.LockFile, _cancellationToken);

    var (exitCode, output) = await RunAsync("migrate");

    Assert.True(exitCode is 0, output);
    Assert.Contains("is already at version", output, StringComparison.Ordinal);
    Assert.Equal(text, await File.ReadAllTextAsync(_paths.LockFile, _cancellationToken));
  }

  [Fact]
  public async Task Execute_Relock_StartsTheLockOver() {
    _ = await RunAsync("migrate");

    var (exitCode, output) = await RunAsync("migrate", "--relock");

    Assert.True(exitCode is 0, output);
    Assert.True(File.Exists(_paths.LockFile));
  }

  [Fact]
  public async Task Execute_LayersThatApply_WritesTheLock() {
    CopyRealLayers();

    var (exitCode, output) = await RunAsync("migrate");

    Assert.True(exitCode is 0, output);
    Assert.True(File.Exists(_paths.LockFile));
  }

  [Fact]
  public async Task Execute_LayerThatCannotBeRead_FailsWritesNoLockAndSaysWhy() {
    await File.WriteAllTextAsync(
      Path.Combine(_paths.Seeds, "foo.yaml"),
      "description: Foo\nextends: nope\n",
      _cancellationToken);

    var (exitCode, output) = await RunAsync("migrate");

    Assert.Equal(1, exitCode);
    Assert.Contains("nope", output, StringComparison.Ordinal);
    Assert.False(File.Exists(_paths.LockFile));
  }
}
