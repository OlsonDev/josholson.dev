using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Migrate;

// A branch measured from a base (`origin/main`): the lock and layers are derived from the base's and the branch's
// migrations, so a migration main has not locked can be edited again and again, and a layer the branch's author wrote is
// kept. The base is a snapshot a test makes up (`FakeSnapshotSource`), taken at the point the branch starts from: main
// has the layers at version 1 (`FooScenarios.VersionOneWithLayers`), and the branch has added the note migration and
// moved forward from the working tree (`FooScenarios.VersionTwo`).
public sealed class LayerBaseDerivationTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerBaseDerivationTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  [Fact]
  public async Task MigrateAsync_AnUnmergedMigrationEditedAgain_RegeneratesTheLayersFromTheBase() {
    var main = await _repository.BranchFromVersionOneAsync();
    await _repository.WriteNoteMigrationAsync("y");

    var result = await _repository.MigrateAsync();

    Assert.Equal(main.Description, result.Base);
    Assert.Equal([("base", true), ("child", true)], result.Layers);
    Assert.Contains("note: y", await _repository.ReadLayerAsync("base"), StringComparison.Ordinal);
    var onDisk = await MigrationFiles.LoadAsync(_repository.Options.MigrationsDirectory, TestContext.Current.CancellationToken);
    Assert.True(SchemaLockParser.Parse(await _repository.ReadLockAsync()).Check(onDisk).IsConsistent);
    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }

  // `extra` is a layer the branch wrote and `child` one of the base's that it edited, so both are the author's.
  [Fact]
  public async Task MigrateAsync_ALayerTheBranchWroteAndABaseLayerItEdited_AreKeptAsTheyAreWhileTheOthersAreRegenerated() {
    _ = await _repository.BranchFromVersionOneAsync();
    await _repository.WriteLayerAsync("extra", FooSchema.ExtraLayer("x"));
    var extra = await _repository.ReadLayerAsync("extra");
    await _repository.WriteLayerAsync("child", FooSchema.ChildLayer("x", FooSchema.Insert(1002, "Four", "x")));
    var child = await _repository.ReadLayerAsync("child");
    await _repository.WriteNoteMigrationAsync("y");

    var result = await _repository.MigrateAsync();

    Assert.Equal(["child", "extra"], result.Kept);
    Assert.Equal([("base", true)], result.Layers);
    Assert.Equal(extra, await _repository.ReadLayerAsync("extra"));
    Assert.Equal(child, await _repository.ReadLayerAsync("child"));
    Assert.Contains("note: y", await _repository.ReadLayerAsync("base"), StringComparison.Ordinal);
    Assert.Equal([await _repository.ChecksumOfAsync("base")], await _repository.RecordedLayersAsync());
  }
}
