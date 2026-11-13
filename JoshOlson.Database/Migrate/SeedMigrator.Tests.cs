using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Migrate;

// The lock's life (none yet, already right, behind the migrations, a migration changed under it, started over) and the
// check of layers that are not moved, over the small schema of `FakeSeedRepository`: none of it is about the real
// schema, whose migrations and lock `SeedToolCheckTests` covers. What a new migration does to the lock and the layers
// is in `LayerMoverTests`.
public sealed class SeedMigratorTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly FakeSeedRepository _repository;

  public SeedMigratorTests()
    => _repository = new(_cancellationToken);

  public void Dispose()
    => _repository.Dispose();

  // The one test that starts from nothing: what it makes is `FooScenarios.VersionOne`.
  [Fact]
  public async Task MigrateAsync_NoLockAndThenASecondRun_WritesALockAtTheNewestMigrationAndThenChangesNothing() {
    await _repository.WriteBarMigrationAsync();

    var first = await _repository.MigrateAsync();
    var text = await _repository.ReadLockAsync();
    var second = await _repository.MigrateAsync();

    var schemaLock = SchemaLockParser.Parse(text);
    var onDisk = await MigrationFiles.LoadAsync(_repository.Options.MigrationsDirectory, _cancellationToken);
    Assert.Equal(_repository.Workspace.LockFile, first.Path);
    Assert.True(first.Changed);
    Assert.Null(first.PreviousVersion);
    Assert.Equal(onDisk.Max(migration => migration.Version), schemaLock.Version);
    Assert.Equal(schemaLock.Version, first.Version);
    Assert.True(schemaLock.Check(onDisk).IsConsistent);
    Assert.NotEmpty(schemaLock.Tables);
    Assert.False(File.Exists(first.Path + ".tmp"));
    Assert.False(second.Changed);
    Assert.Equal(first.Version, second.PreviousVersion);
    Assert.Equal(text, await _repository.ReadLockAsync());
  }

  [Fact]
  public async Task MigrateAsync_LockOlderThanTheMigrations_IsRestamped() {
    var start = await _repository.StartAtAsync(FooScenarios.VersionOne);
    await _repository.WriteNoteMigrationAsync();

    var result = await _repository.MigrateAsync();

    Assert.True(result.Changed);
    Assert.Equal(start.Result.Version, result.PreviousVersion);
    Assert.Equal(start.Result.Version + 1, SchemaLockParser.Parse(await _repository.ReadLockAsync()).Version);
  }

  // The comment's text is fixed, so the edited migration's template is built once and stays on the server.
  [Fact]
  public async Task MigrateAsync_MigrationChangedSinceTheLock_WritesNothingAndSaysWhichUntilRelockAcceptsIt() {
    var start = await _repository.StartAtAsync(FooScenarios.VersionOne);
    await File.AppendAllTextAsync(_repository.Migration(FooSchema.BarFile), "\n-- edited\n", _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(_repository.MigrateAsync);
    var refused = await _repository.ReadLockAsync();
    var result = await _repository.MigrateAsync(_repository.Options with { Relock = true });

    Assert.Contains("V001__", exception.Message, StringComparison.Ordinal);
    Assert.Contains("has changed", exception.Message, StringComparison.Ordinal);
    Assert.Equal(start.LockText, refused);
    var schemaLock = SchemaLockParser.Parse(await _repository.ReadLockAsync());
    var onDisk = await MigrationFiles.LoadAsync(_repository.Options.MigrationsDirectory, _cancellationToken);
    Assert.True(result.Changed);
    Assert.Equal(start.Result.Version, result.PreviousVersion);
    Assert.True(schemaLock.Check(onDisk).IsConsistent);
  }

  // A layer the lock is not behind, so it is checked and not moved: the lock says nothing about layers yet.
  private async Task<FooScenario> StartWithALayerAsync() {
    var start = await _repository.StartAtAsync(FooScenarios.VersionOne);
    await _repository.WriteLayerAsync("base", FooSchema.BaseLayer());
    return start;
  }

  [Fact]
  public async Task MigrateAsync_SeedLayerPresentAndNoWayToCheckIt_Refuses() {
    var start = await StartWithALayerAsync();

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => _repository.MigrateAsync(_repository.Options with { LayerCheck = null }));

    Assert.Contains("1 layer(s)", exception.Message, StringComparison.Ordinal);
    Assert.Equal(start.LockText, await _repository.ReadLockAsync());
  }

  [Fact]
  public async Task MigrateAsync_LayerCheckReportsProblems_RefusesAndWritesNothing() {
    var start = await StartWithALayerAsync();
    var options = _repository.Options with {
      LayerCheck = (_, _) => Task.FromResult<List<string>>(["base: foo.bar has no column baz"]),
    };

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => _repository.MigrateAsync(options));

    Assert.Contains("base: foo.bar has no column baz", exception.Message, StringComparison.Ordinal);
    Assert.Equal(start.LockText, await _repository.ReadLockAsync());
  }

  [Fact]
  public async Task MigrateAsync_LayerCheckFindsNothingWrong_WritesTheLockAndChecksAgainstTheNewSchema() {
    _ = await StartWithALayerAsync();
    SeedSchema? seen = null;
    var options = _repository.Options with {
      LayerCheck = (schema, _) => {
        seen = schema;
        return Task.FromResult<List<string>>([]);
      },
    };

    var result = await _repository.MigrateAsync(options);

    Assert.True(result.Changed);
    Assert.NotNull(seen?.Find("foo.bar"));
  }
}
