using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Migrate;

// What a migration does to layers that move cleanly, seen from the files (see `FakeSeedRepository`). Each test starts
// with the layers at version 1 (`FooScenarios`) and is one run.
public sealed class LayerMoverTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerMoverTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  // The run is the one that made `FooScenarios.VersionTwo`, so the test makes none of its own.
  [Fact]
  public async Task MigrateAsync_ANotNullColumnWithADefault_IsNamedInEveryInsertAndTheLayersAreRecordedAsWritten() {
    var moved = await _repository.StartAtAsync(FooScenarios.VersionTwo);

    var result = moved.Result;

    Assert.Equal([("base", true), ("child", true)], result.Layers);
    Assert.Equal(2, result.Version);
    Assert.Equal(
      """
      description: Base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1, name: One, note: x }
            - { bar_id: 2, name: Two, note: x }

      """,
      await _repository.ReadLayerAsync("base"));
    Assert.Equal(
      """
      description: Child
      extends: base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1001, name: Three, note: x }
          update:
            - { bar_id: 1, name: Uno }

      """,
      await _repository.ReadLayerAsync("child"));
    Assert.Equal(2, SchemaLockParser.Parse(await _repository.ReadLockAsync()).Version);
    Assert.Equal(
      [await _repository.ChecksumOfAsync("base"), await _repository.ChecksumOfAsync("child")],
      await _repository.RecordedLayersAsync());
  }

  [Fact]
  public async Task MigrateAsync_AMigrationThatChangesData_ChangesTheLayers() {
    _ = await _repository.StartAtAsync(FooScenarios.VersionOneWithLayers);
    await _repository.WriteMigrationAsync("V002__upper.sql", "UPDATE foo.bar SET name = upper(name);\n");

    _ = await _repository.MigrateAsync();

    Assert.Contains("{ bar_id: 1, name: ONE }", await _repository.ReadLayerAsync("base"), StringComparison.Ordinal);
    var child = await _repository.ReadLayerAsync("child");
    Assert.Contains("{ bar_id: 1001, name: THREE }", child, StringComparison.Ordinal);
    Assert.Contains("{ bar_id: 1, name: UNO }", child, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MigrateAsync_AMigrationThatLeavesALayerAsItWas_DoesNotRewriteTheFileAndLocksTheNewSchema() {
    _ = await _repository.StartAtAsync(FooScenarios.VersionOneWithLayers);
    var before = await _repository.ReadLayerAsync("base");
    await _repository.WriteMigrationAsync(
      "V002__baz.sql",
      "CREATE TABLE foo.baz (baz_id INT NOT NULL, CONSTRAINT pk_baz PRIMARY KEY (baz_id));\n");

    var result = await _repository.MigrateAsync();

    Assert.Equal([("base", false), ("child", false)], result.Layers);
    Assert.Equal(before, await _repository.ReadLayerAsync("base"));
    Assert.Equal(2, result.Version);
    // The new schema is read from a template built from the new migration: no dev database is involved.
    var schemaLock = SchemaLockParser.Parse(await _repository.ReadLockAsync());
    Assert.Contains(schemaLock.Tables, table => table.Name is "foo.baz");
  }
}
