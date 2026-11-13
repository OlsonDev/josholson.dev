using Xunit;

namespace JoshOlson.Database.Migrate;

// The starting points the tests of `seeds:migrate` share, each built once per test run by real runs of
// `SeedMigrator.MigrateAsync` and then stamped into every test's own directory (`FakeSeedRepository.StartAtAsync`), so
// a test pays for the one run it is about and not for the two to four that lead up to it. A starting point is only
// files: the templates behind it are named by a hash of them and stay on the server. Each builds on the one before it,
// in a workspace of its own that is gone when it is done.
//
// They are built with no token of a test's, so a test that is canceled does not leave a canceled task for the others.
internal static class FooScenarios {
  private static readonly Lazy<Task<FooScenario>> _VersionOne = new(
    () => BuildVersionOneAsync(CancellationToken.None));

  private static readonly Lazy<Task<FooScenario>> _VersionOneWithLayers = new(
    () => BuildVersionOneWithLayersAsync(VersionOne, CancellationToken.None));

  private static readonly Lazy<Task<FooScenario>> _VersionTwo = new(
    () => BuildAsync(VersionOneWithLayers, repository => repository.WriteNoteMigrationAsync(), CancellationToken.None));

  private static readonly Lazy<Task<FooScenario>> _KindVersionOne = new(
    () => BuildAsync(start: null, repository => repository.WriteKindMigrationAsync(), CancellationToken.None));

  // The bar migration, locked. No layers.
  public static Task<FooScenario> VersionOne
    => _VersionOne.Value;

  // `VersionOne` and the layers `base` and `child`, which the lock records as the tool's own.
  public static Task<FooScenario> VersionOneWithLayers
    => _VersionOneWithLayers.Value;

  // `VersionOneWithLayers` and the note migration (its default is `x`), with both layers moved to it.
  public static Task<FooScenario> VersionTwo
    => _VersionTwo.Value;

  // The kind migration and its repeatable, locked. No layers.
  public static Task<FooScenario> KindVersionOne
    => _KindVersionOne.Value;

  // The first two again, for the test that they come out the same wherever and whenever they are built.
  public static async Task<FooScenario> BuildVersionOneAsync(CancellationToken cancellationToken)
    => await BuildAsync(start: null, repository => repository.WriteBarMigrationAsync(), cancellationToken);

  // The lock is made first (`start`), which the layers are read against; this run finds them and records them.
  public static async Task<FooScenario> BuildVersionOneWithLayersAsync(
    Task<FooScenario> start,
    CancellationToken cancellationToken
  )
    => await BuildAsync(start, repository => repository.WriteLayersAsync(), cancellationToken);

  // One step on from `start` (or from nothing): its files, what `change` writes, and one run.
  private static async Task<FooScenario> BuildAsync(
    Task<FooScenario>? start,
    Func<FakeSeedRepository, Task> change,
    CancellationToken cancellationToken
  ) {
    using var repository = new FakeSeedRepository(cancellationToken);
    if (start is not null) {
      _ = await repository.StartAtAsync(start);
    }
    await change(repository);
    var result = await repository.MigrateAsync();
    return new(await repository.Workspace.SnapshotFilesAsync(cancellationToken), result);
  }
}

public sealed class FooScenariosTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  // What the cache rests on: nothing in a lock or a layer depends on the directory it was written in or on the time.
  [Fact]
  public async Task VersionOneWithLayers_BuiltAgainInAnotherWorkspace_IsTheSameFilesByteForByte() {
    var cached = await FooScenarios.VersionOneWithLayers;

    var again = await FooScenarios.BuildVersionOneWithLayersAsync(
      FooScenarios.BuildVersionOneAsync(_cancellationToken),
      _cancellationToken);

    Assert.Equal(["base", "child"], again.AsSnapshot().Layers.Keys.Order(StringComparer.Ordinal));
    Assert.Equal(cached.Files.Keys.Order(StringComparer.Ordinal), again.Files.Keys.Order(StringComparer.Ordinal));
    foreach (var (file, text) in cached.Files) {
      Assert.Equal(text, again.Files[file]);
    }
    Assert.NotEqual(cached.Result.Path, again.Result.Path);
  }

  [Fact]
  public async Task AsSnapshot_ScenarioWithLayers_IsItsLockAndItsLayersByName() {
    using var repository = new FakeSeedRepository(_cancellationToken);
    var scenario = await repository.StartAtAsync(FooScenarios.VersionOneWithLayers);

    var snapshot = scenario.AsSnapshot();

    Assert.Equal(await repository.ReadLockAsync(), snapshot.LockText);
    Assert.Equal(["base", "child"], snapshot.Layers.Keys.Order(StringComparer.Ordinal));
    Assert.Equal(await repository.ReadLayerAsync("base"), snapshot.Layers["base"]);
    Assert.Equal(await repository.ReadLayerAsync("child"), snapshot.Layers["child"]);
    var now = await repository.SnapshotNowAsync();
    Assert.Equal(now.Description, snapshot.Description);
    Assert.Equal(now.Commit, snapshot.Commit);
  }
}
