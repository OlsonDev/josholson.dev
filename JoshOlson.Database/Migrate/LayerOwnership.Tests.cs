using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Migrate;

// Which layer files the lock records as the tool's own, and which stay somebody's (see `FakeSeedRepository`).
public sealed class LayerOwnershipTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerOwnershipTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  // The layers were first found by the run that made the scenario, when the lock said nothing about layers: that run
  // moved none and recorded both as the tool's. The run here finds every layer recorded, so it has no reason to read
  // the base.
  [Fact]
  public async Task MigrateAsync_NothingPending_LeavesTheLayersRecordsThemAndNeverReadsTheBase() {
    var start = await _repository.StartAtAsync(FooScenarios.VersionOneWithLayers);
    var recorded = await _repository.RecordedLayersAsync();
    _repository.UseBase(start.AsSnapshot());

    var result = await _repository.MigrateAsync();

    Assert.Empty(start.Result.Layers);
    Assert.Equal([await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")], recorded);
    Assert.Empty(result.Layers);
    Assert.False(result.Changed);
    Assert.Equal(1, SchemaLockParser.Parse(await _repository.ReadLockAsync()).Version);
    Assert.Equal(recorded, await _repository.RecordedLayersAsync());
    Assert.Equal(0, ((FakeSnapshotSource)_repository.Options.Snapshots!).Reads);
  }

  // `child` was edited since the tool wrote it and `extra` was added after the lock recorded the others, so neither is
  // the tool's: both are moved like the rest, and only `base` is recorded, as it was written this time.
  [Fact]
  public async Task MigrateAsync_LayerEditedAndLayerAddedSinceTheLockRecordedThem_AreMovedLikeTheRestButNotRecorded() {
    _ = await _repository.StartAtAsync(FooScenarios.VersionOneWithLayers);
    var recorded = await _repository.ChecksumOfAsync("base");
    await _repository.WriteLayerAsync("child", FooSchema.ChildLayer(extraInserts: FooSchema.Insert(1002, "Four")));
    await _repository.WriteLayerAsync("extra", FooSchema.ExtraLayer());
    await _repository.WriteNoteMigrationAsync();

    var result = await _repository.MigrateAsync();

    Assert.Contains(
      "{ bar_id: 1002, name: Four, note: x }",
      await _repository.ReadLayerAsync("child"),
      StringComparison.Ordinal);
    Assert.Contains(("extra", true), result.Layers);
    Assert.Equal(FooSchema.ExtraLayer("x"), await _repository.ReadLayerAsync("extra"));
    Assert.Equal([await _repository.ChecksumOfAsync("base")], await _repository.RecordedLayersAsync());
    Assert.NotEqual(recorded, await _repository.ChecksumOfAsync("base"));
  }
}
