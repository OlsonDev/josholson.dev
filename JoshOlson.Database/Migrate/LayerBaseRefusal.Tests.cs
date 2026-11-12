using Xunit;

namespace JoshOlson.Database.Migrate;

// What a run measured from a base refuses, and writes nothing for (see `LayerBaseDerivationTests`).
public sealed class LayerBaseRefusalTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerBaseRefusalTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  [Fact]
  public async Task MigrateAsync_AMigrationTheBaseHasLocked_ThatWasEdited_IsRefusedAndNothingIsWritten() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    await _repository.WriteNoteMigrationAsync();
    _ = await _repository.MigrateAsync();
    var main = await _repository.SnapshotNowAsync();
    var lockBefore = await _repository.ReadAsync(_repository.LockFile);
    var baseBefore = await _repository.ReadAsync(_repository.Layer("base"));
    await _repository.WriteAsync(_repository.Migration("V002__note.sql"), FakeSeedRepository.NoteDefaultingToY);
    _repository.UseBase(main);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains($"{main.Description} has locked migrations your branch does not agree with", exception.Message, StringComparison.Ordinal);
    Assert.Contains("V002__note.sql", exception.Message, StringComparison.Ordinal);
    Assert.Equal(lockBefore, await _repository.ReadAsync(_repository.LockFile));
    Assert.Equal(baseBefore, await _repository.ReadAsync(_repository.Layer("base")));
  }

  [Fact]
  public async Task MigrateAsync_AMigrationTheBaseHasThatTheBranchLacks_SaysHowToBringItIn() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    await _repository.WriteNoteMigrationAsync();
    _ = await _repository.MigrateAsync();
    var main = await _repository.SnapshotNowAsync();
    File.Delete(_repository.Migration("V002__note.sql"));
    _repository.UseBase(main);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains($"git checkout {main.Commit} -- ", exception.Message, StringComparison.Ordinal);
    Assert.Contains("V002__note.sql", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MigrateAsync_ALayerTheBranchWroteThatTheNewSchemaBreaks_IsAHardErrorNamingIt() {
    var main = await _repository.BranchWithAMigrationAsync();
    await _repository.WriteAuthoredLayerAsync("x");
    var lockBefore = await _repository.ReadAsync(_repository.LockFile);
    // The column is renamed in the edit, so the branch's own layer names one that is no longer there.
    await _repository.WriteAsync(_repository.Migration("V002__note.sql"), "ALTER TABLE foo.bar ADD COLUMN memo TEXT NOT NULL DEFAULT 'x';\n");
    _repository.UseBase(main);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains("extra:", exception.Message, StringComparison.Ordinal);
    Assert.Contains("your own file", exception.Message, StringComparison.Ordinal);
    Assert.Equal(lockBefore, await _repository.ReadAsync(_repository.LockFile));
  }
}
