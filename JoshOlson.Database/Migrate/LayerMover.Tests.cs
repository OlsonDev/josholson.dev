using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Migrate;

// What a migration does to layers that move cleanly, seen from the files (see `FakeSeedRepository`).
public sealed class LayerMoverTests : IDisposable {
  private readonly FakeSeedRepository _repository;

  public LayerMoverTests()
    => _repository = new(TestContext.Current.CancellationToken);

  public void Dispose()
    => _repository.Dispose();

  [Fact]
  public async Task MigrateAsync_NothingPending_LeavesTheLayersAlone() {
    var result = await _repository.MigrateWithLayersAtVersionOneAsync();

    Assert.Empty(result.Layers);
    Assert.Equal(1, SchemaLockParser.Parse(await _repository.ReadAsync(_repository.LockFile)).Version);
  }

  [Fact]
  public async Task MigrateAsync_ANotNullColumnWithADefault_IsNamedInEveryInsert() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    await _repository.WriteNoteMigrationAsync();

    var result = await _repository.MigrateAsync();

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
      await _repository.ReadAsync(_repository.Layer("base")));
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
      await _repository.ReadAsync(_repository.Layer("child")));
    Assert.Equal(2, SchemaLockParser.Parse(await _repository.ReadAsync(_repository.LockFile)).Version);
  }

  [Fact]
  public async Task MigrateAsync_AMigrationThatChangesData_ChangesTheLayers() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    await _repository.WriteAsync(_repository.Migration("V002__upper.sql"), "UPDATE foo.bar SET name = upper(name);\n");

    _ = await _repository.MigrateAsync();

    Assert.Contains("{ bar_id: 1, name: ONE }", await _repository.ReadAsync(_repository.Layer("base")), StringComparison.Ordinal);
    var child = await _repository.ReadAsync(_repository.Layer("child"));
    Assert.Contains("{ bar_id: 1001, name: THREE }", child, StringComparison.Ordinal);
    Assert.Contains("{ bar_id: 1, name: UNO }", child, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MigrateAsync_AMigrationThatLeavesALayerAsItWas_DoesNotRewriteTheFile() {
    _ = await _repository.MigrateWithLayersAtVersionOneAsync();
    var before = await _repository.ReadAsync(_repository.Layer("base"));
    await _repository.WriteAsync(
      _repository.Migration("V002__baz.sql"),
      "CREATE TABLE foo.baz (baz_id INT NOT NULL, CONSTRAINT pk_baz PRIMARY KEY (baz_id));\n");

    var result = await _repository.MigrateAsync();

    Assert.Equal([("base", false), ("child", false)], result.Layers);
    Assert.Equal(before, await _repository.ReadAsync(_repository.Layer("base")));
    Assert.Equal(2, result.Version);
  }
}
