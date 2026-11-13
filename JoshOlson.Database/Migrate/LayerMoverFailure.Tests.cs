using Xunit;

namespace JoshOlson.Database.Migrate;

// What a migration does to layers it cannot move, or moves only past the old schema's missing reference data (see
// `FakeSeedRepository`). A run that fails writes nothing.
public sealed class LayerMoverFailureTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerMoverFailureTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  [Fact]
  public async Task MigrateAsync_AMigrationThatFailsOnALayersRows_NamesTheLayerAndWritesNothing() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    var lockBefore = await _repository.ReadLockAsync();
    var baseBefore = await _repository.ReadLayerAsync("base");
    await _repository.WriteMigrationAsync(
      "V002__check.sql",
      "ALTER TABLE foo.bar ADD CONSTRAINT ck_bar_name CHECK (name <> 'Two');\n");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains("base: the migrations failed on its rows", exception.Message, StringComparison.Ordinal);
    Assert.DoesNotContain("child:", exception.Message, StringComparison.Ordinal);
    Assert.Equal(lockBefore, await _repository.ReadLockAsync());
    Assert.Equal(baseBefore, await _repository.ReadLayerAsync("base"));
  }

  [Fact]
  public async Task MigrateAsync_RowThatPointsAtReferenceDataTheOldSchemaCouldNotHave_IsMovedAndCheckedAtTheNewestVersion() {
    await _repository.WriteKindMigrationAsync();
    _ = await _repository.MigrateAsync();
    await _repository.WriteLayerAsync("base", FooSchema.KindLayer(1));
    await _repository.WriteKindNoteMigrationAsync();

    var result = await _repository.MigrateAsync();

    Assert.Equal([("base", false)], result.Layers);
    Assert.Equal(2, result.Version);
  }

  [Fact]
  public async Task MigrateAsync_RowThatPointsAtReferenceDataTheNewestVersionDoesNotHave_NamesTheLayerAndTheSkippedRepeatable() {
    await _repository.WriteKindMigrationAsync();
    _ = await _repository.MigrateAsync();
    await _repository.WriteLayerAsync("base", FooSchema.KindLayer(2));
    await _repository.WriteKindNoteMigrationAsync();

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);

    Assert.Contains("base: does not build at the newest version", exception.Message, StringComparison.Ordinal);
    Assert.Contains("R__kind.sql", exception.Message, StringComparison.Ordinal);
  }
}
