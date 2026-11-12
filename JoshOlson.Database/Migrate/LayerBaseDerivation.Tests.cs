using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Migrate;

// A branch measured from a base (`origin/main`): the lock and layers are derived from the base's and the branch's
// migrations, so a migration main has not locked can be edited again and again, and a layer the branch's author wrote is
// kept. The base is a snapshot a test makes up (`FakeSnapshotSource`), taken at the point the branch starts from.
public sealed class LayerBaseDerivationTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerBaseDerivationTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  [Fact]
  public async Task MigrateAsync_AnUnmergedMigrationEditedAgain_RegeneratesTheLayersFromTheBase() {
    var main = await _repository.BranchWithAMigrationAsync();
    await _repository.WriteAsync(_repository.Migration("V002__note.sql"), FakeSeedRepository.NoteDefaultingToY);
    _repository.UseBase(main);

    var result = await _repository.MigrateAsync();

    Assert.Equal(main.Description, result.Base);
    Assert.Equal([("base", true), ("child", true)], result.Layers);
    Assert.Contains("note: y", await _repository.ReadAsync(_repository.Layer("base")), StringComparison.Ordinal);
    var onDisk = await MigrationFiles.LoadAsync(_repository.Options.MigrationsDirectory, TestContext.Current.CancellationToken);
    Assert.True(SchemaLockParser.Parse(await _repository.ReadAsync(_repository.LockFile)).Check(onDisk).IsConsistent);
    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }

  [Fact]
  public async Task MigrateAsync_ALayerTheBranchWrote_IsKeptAsItIsAndNotRecorded() {
    var main = await _repository.BranchWithAMigrationAsync();
    await _repository.WriteAuthoredLayerAsync("x");
    var before = await _repository.ReadAsync(_repository.Layer("extra"));
    await _repository.WriteAsync(_repository.Migration("V002__note.sql"), FakeSeedRepository.NoteDefaultingToY);
    _repository.UseBase(main);

    var result = await _repository.MigrateAsync();

    Assert.Equal(["extra"], result.Kept);
    Assert.DoesNotContain(result.Layers, layer => layer.Layer is "extra");
    Assert.Equal(before, await _repository.ReadAsync(_repository.Layer("extra")));
    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }

  [Fact]
  public async Task MigrateAsync_ABaseLayerTheBranchEdited_IsKeptWhileTheOthersAreRegenerated() {
    var main = await _repository.BranchWithAMigrationAsync();
    await _repository.WriteAsync(
      _repository.Layer("child"),
      """
      description: Child
      extends: base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1001, name: Three, note: x }
            - { bar_id: 1002, name: Four, note: x }
          update:
            - { bar_id: 1, name: Uno }

      """);
    var edited = await _repository.ReadAsync(_repository.Layer("child"));
    await _repository.WriteAsync(_repository.Migration("V002__note.sql"), FakeSeedRepository.NoteDefaultingToY);
    _repository.UseBase(main);

    var result = await _repository.MigrateAsync();

    Assert.Equal(["child"], result.Kept);
    Assert.Equal(edited, await _repository.ReadAsync(_repository.Layer("child")));
    Assert.Contains("note: y", await _repository.ReadAsync(_repository.Layer("base")), StringComparison.Ordinal);
  }
}
