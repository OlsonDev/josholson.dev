using Xunit;

namespace JoshOlson.Database.Migrate;

// Which layer files the lock records as the tool's own, and which stay somebody's (see `FakeSeedRepository`).
public sealed class LayerOwnershipTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerOwnershipTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  [Fact]
  public async Task MigrateAsync_LayersTheLockSaysNothingAbout_AreTakenToBeTheToolsAndRecorded() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();

    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }

  [Fact]
  public async Task MigrateAsync_MovedLayers_AreRecordedAsTheyWereWritten() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    await _repository.WriteNoteMigrationAsync();

    _ = await _repository.MigrateAsync();

    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }

  [Fact]
  public async Task MigrateAsync_LayerEditedSinceTheToolWroteIt_IsMovedLikeTheRestButNotRecorded() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    var recorded = await _repository.ChecksumOfAsync("base");
    await _repository.WriteAsync(
      _repository.Layer("child"),
      """
      description: Child
      extends: base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1001, name: Three }
            - { bar_id: 1002, name: Four }
          update:
            - { bar_id: 1, name: Uno }

      """);
    await _repository.WriteNoteMigrationAsync();

    _ = await _repository.MigrateAsync();

    Assert.Contains(
      "{ bar_id: 1002, name: Four, note: x }",
      await _repository.ReadAsync(_repository.Layer("child")),
      StringComparison.Ordinal);
    Assert.Equal([await _repository.ChecksumOfAsync("base")], await _repository.RecordedLayersAsync());
    Assert.NotEqual(recorded, await _repository.ChecksumOfAsync("base"));
  }

  [Fact]
  public async Task MigrateAsync_LayerAddedAfterTheLockRecordedTheOthers_IsMovedButNotRecorded() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    await _repository.WriteAsync(
      _repository.Layer("extra"),
      """
      description: Extra
      extends: base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1001, name: Extra }

      """);
    await _repository.WriteNoteMigrationAsync();

    var result = await _repository.MigrateAsync();

    Assert.Contains(("extra", true), result.Layers);
    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }
}
