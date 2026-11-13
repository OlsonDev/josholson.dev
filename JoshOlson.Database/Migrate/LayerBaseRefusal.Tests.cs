using Xunit;

namespace JoshOlson.Database.Migrate;

// What a run measured from a base refuses, and writes nothing for (see `LayerBaseDerivationTests`).
public sealed class LayerBaseRefusalTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerBaseRefusalTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  // Main has the note migration and has locked it: the base is the branch's own starting point.
  [Fact]
  public async Task MigrateAsync_AMigrationTheBaseHasLocked_ThatWasEdited_IsRefusedAndNothingIsWritten() {
    var main = (await _repository.StartAtAsync(FooScenarios.VersionTwo)).AsSnapshot();
    var lockBefore = await _repository.ReadLockAsync();
    var baseBefore = await _repository.ReadLayerAsync("base");
    await _repository.WriteNoteMigrationAsync("y");
    _repository.UseBase(main);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains($"{main.Description} has locked migrations your branch does not agree with", exception.Message, StringComparison.Ordinal);
    Assert.Contains("V002__note.sql", exception.Message, StringComparison.Ordinal);
    Assert.Equal(lockBefore, await _repository.ReadLockAsync());
    Assert.Equal(baseBefore, await _repository.ReadLayerAsync("base"));
  }

  [Fact]
  public async Task MigrateAsync_AMigrationTheBaseHasThatTheBranchLacks_SaysHowToBringItIn() {
    var main = (await _repository.StartAtAsync(FooScenarios.VersionTwo)).AsSnapshot();
    File.Delete(_repository.Migration(FooSchema.NoteFile));
    _repository.UseBase(main);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains($"git checkout {main.Commit} -- ", exception.Message, StringComparison.Ordinal);
    Assert.Contains("V002__note.sql", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MigrateAsync_ALayerTheBranchWroteThatTheNewSchemaBreaks_IsAHardErrorNamingIt() {
    _ = await _repository.BranchFromVersionOneAsync();
    await _repository.WriteLayerAsync("extra", FooSchema.ExtraLayer("x"));
    var lockBefore = await _repository.ReadLockAsync();
    // The column is renamed in the edit, so the branch's own layer names one that is no longer there.
    await _repository.WriteMigrationAsync(
      FooSchema.NoteFile,
      "ALTER TABLE foo.bar ADD COLUMN memo TEXT NOT NULL DEFAULT 'x';\n");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains("extra:", exception.Message, StringComparison.Ordinal);
    Assert.Contains("your own file", exception.Message, StringComparison.Ordinal);
    Assert.Equal(lockBefore, await _repository.ReadLockAsync());
  }
}
