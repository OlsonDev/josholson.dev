using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Migrate;

// Reads the schema from the template for a copy of the repository's migrations, which is the real template while the
// files are the same and one built for the test when they are not (and dropped when it ends). Each test works in a
// `SeedWorkspace` with the real migrations and no lock or layers, so the real lock is never touched.
public sealed class SeedMigratorTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedWorkspace _workspace = SeedWorkspace.CopyOfRepository("seed-migrator", withLock: false, withLayers: false);
  private readonly MigrateOptions _options;

  public SeedMigratorTests()
    => _options = _workspace.MigrateOptions;

  public ValueTask InitializeAsync()
    => ValueTask.CompletedTask;

  // A test that changed a migration made a template of its own, which goes, so only the real one is left.
  public async ValueTask DisposeAsync() {
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(CancellationToken.None);
    var name = await _workspace.Template.NameAsync(CancellationToken.None);
    var real = await SeedTestServer.RealTemplate.NameAsync(CancellationToken.None);
    if (!string.Equals(name, real, StringComparison.Ordinal)) {
      await SeedTestServer.Databases.DropAsync(owner, name, CancellationToken.None);
    }
    _workspace.Dispose();
  }

  private async Task<MigrateResult> MigrateAsync(
    bool relock = false,
    Func<SeedSchema, CancellationToken, Task<List<string>>>? check = null
  ) {
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    return await SeedMigrator.MigrateAsync(
      owner,
      _options with { Relock = relock, LayerCheck = check },
      _cancellationToken);
  }

  [Fact]
  public async Task MigrateAsync_NoLock_WritesALockAtTheNewestMigration() {
    var result = await MigrateAsync();

    var schemaLock = SchemaLockParser.Parse(await File.ReadAllTextAsync(result.Path, _cancellationToken));
    var onDisk = await MigrationFiles.LoadAsync(_options.MigrationsDirectory, _cancellationToken);
    Assert.True(result.Changed);
    Assert.Null(result.PreviousVersion);
    Assert.Equal(onDisk.Max(migration => migration.Version), schemaLock.Version);
    Assert.Equal(schemaLock.Version, result.Version);
    Assert.True(schemaLock.Check(onDisk).IsConsistent);
    Assert.NotEmpty(schemaLock.Tables);
    Assert.False(File.Exists(result.Path + ".tmp"));
  }

  [Fact]
  public async Task MigrateAsync_SecondRun_ChangesNothing() {
    var first = await MigrateAsync();
    var text = await File.ReadAllTextAsync(first.Path, _cancellationToken);

    var second = await MigrateAsync();

    Assert.False(second.Changed);
    Assert.Equal(first.Version, second.PreviousVersion);
    Assert.Equal(text, await File.ReadAllTextAsync(second.Path, _cancellationToken));
  }

  [Fact]
  public async Task MigrateAsync_LockOlderThanTheMigrations_IsRestamped() {
    var first = await MigrateAsync();
    var current = SchemaLockParser.Parse(await File.ReadAllTextAsync(first.Path, _cancellationToken));
    var older = current with {
      Migrations = [.. current.Migrations.Where(migration => migration.Version < current.Version)],
    };
    await File.WriteAllTextAsync(first.Path, SchemaLockWriter.Write(older), _cancellationToken);

    var result = await MigrateAsync();

    Assert.True(result.Changed);
    Assert.Equal(current.Version - 1, result.PreviousVersion);
    Assert.Equal(current.Version, SchemaLockParser.Parse(await File.ReadAllTextAsync(first.Path, _cancellationToken)).Version);
  }

  [Fact]
  public async Task MigrateAsync_MigrationChangedSinceTheLock_WritesNothingAndSaysWhich() {
    var first = await MigrateAsync();
    var text = await File.ReadAllTextAsync(first.Path, _cancellationToken);
    var changed = Directory.EnumerateFiles(_options.MigrationsDirectory, "V001__*.sql").Single();
    await File.AppendAllTextAsync(changed, "\n-- edited\n", _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => MigrateAsync());

    Assert.Contains("V001__", exception.Message, StringComparison.Ordinal);
    Assert.Contains("has changed", exception.Message, StringComparison.Ordinal);
    Assert.Equal(text, await File.ReadAllTextAsync(first.Path, _cancellationToken));
  }

  [Fact]
  public async Task MigrateAsync_Relock_AcceptsAMigrationRewrittenSinceTheLock() {
    var first = await MigrateAsync();
    var changed = Directory.EnumerateFiles(_options.MigrationsDirectory, "V001__*.sql").Single();
    await File.AppendAllTextAsync(changed, "\n-- edited\n", _cancellationToken);

    var result = await MigrateAsync(relock: true);

    var schemaLock = SchemaLockParser.Parse(await File.ReadAllTextAsync(result.Path, _cancellationToken));
    var onDisk = await MigrationFiles.LoadAsync(_options.MigrationsDirectory, _cancellationToken);
    Assert.True(result.Changed);
    Assert.Equal(first.Version, result.PreviousVersion);
    Assert.True(schemaLock.Check(onDisk).IsConsistent);
  }

  [Fact]
  public async Task MigrateAsync_NewMigration_IsReadFromATemplateBuiltFromItAndNoDevelopmentDatabaseIsInvolved() {
    await _workspace.WriteMigrationAsync(
      "V999__probe.sql",
      "CREATE SCHEMA zz_probe;\nCREATE TABLE zz_probe.foo (bar INT NOT NULL);\n",
      _cancellationToken);

    var result = await MigrateAsync();

    var schemaLock = SchemaLockParser.Parse(await File.ReadAllTextAsync(result.Path, _cancellationToken));
    Assert.Equal(999, result.Version);
    Assert.Contains(schemaLock.Tables, table => table.Name is "zz_probe.foo");
  }

  private async Task WriteLayerAsync() {
    const string Layer = """
      description: A layer
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1 }
      """;
    await _workspace.WriteLayerAsync("foo", Layer, _cancellationToken);
  }

  [Fact]
  public async Task MigrateAsync_SeedLayerPresentAndNoWayToCheckIt_Refuses() {
    await WriteLayerAsync();

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => MigrateAsync());

    Assert.Contains("1 layer(s)", exception.Message, StringComparison.Ordinal);
    Assert.False(File.Exists(_workspace.LockFile));
  }

  [Fact]
  public async Task MigrateAsync_LayerCheckReportsProblems_RefusesAndWritesNothing() {
    await WriteLayerAsync();

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => MigrateAsync(check: (_, _) => Task.FromResult<List<string>>(["foo: tag.tag has no column bar"])));

    Assert.Contains("foo: tag.tag has no column bar", exception.Message, StringComparison.Ordinal);
    Assert.False(File.Exists(_workspace.LockFile));
  }

  [Fact]
  public async Task MigrateAsync_LayerCheckFindsNothingWrong_WritesTheLockAndChecksAgainstTheNewSchema() {
    await WriteLayerAsync();
    SeedSchema? seen = null;

    var result = await MigrateAsync(check: (schema, _) => {
      seen = schema;
      return Task.FromResult<List<string>>([]);
    });

    Assert.True(result.Changed);
    Assert.NotNull(seen?.Find("tag.tag"));
  }
}
