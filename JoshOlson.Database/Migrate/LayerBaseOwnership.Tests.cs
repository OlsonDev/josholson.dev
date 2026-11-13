using Xunit;

namespace JoshOlson.Database.Migrate;

// What the base says about whose a layer file is, or whether there is a base at all (see `LayerBaseDerivationTests`).
// That a run where every layer is recorded never reads the base is in `LayerOwnershipTests`.
public sealed class LayerBaseOwnershipTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerBaseOwnershipTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  [Fact]
  public async Task MigrateAsync_ALayerThatIsExactlyTheBasesCopy_IsTheToolsOwnAgainAndRecorded() {
    _ = await _repository.StartAtAsync(FooScenarios.VersionOneWithLayers);
    // The author edits `child` (the lock no longer vouches for it), and the edit is later merged into the base.
    await _repository.WriteLayerAsync("child", FooSchema.ChildLayer(extraInserts: FooSchema.Insert(1002, "Four")));
    var main = await _repository.SnapshotNowAsync();
    _repository.UseBase(main);

    _ = await _repository.MigrateAsync();

    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }

  [Fact]
  public async Task MigrateAsync_BaseGivenAndItHasNoLock_IsRefused() {
    _ = await _repository.StartAtAsync(FooScenarios.VersionOneWithLayers);
    _repository.UseBase(new("origin/main at abc1234 (2026-11-12)", "abc1234def5678", LockText: null, []), explicitBase: "abc1234");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains("has no lock", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MigrateAsync_NoBaseToReadAndTheLockDisagrees_ReportsTheDisagreementAndWhyThereIsNoBase() {
    _ = await _repository.StartAtAsync(FooScenarios.VersionTwo);
    await _repository.WriteNoteMigrationAsync("y");
    _repository.UseBase(snapshot: null, warnings: ["There is no origin/main, so there is no base to derive the seeds from."]);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains("has changed since the seeds were migrated against it", exception.Message, StringComparison.Ordinal);
    Assert.Contains("There is no origin/main", exception.Message, StringComparison.Ordinal);
  }
}
