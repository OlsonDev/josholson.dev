using JoshOlson.Database.Locks;
using JoshOlson.Shared.IO;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Migrate;

// Runs against the dev database, which must have the repository's migrations applied (`mise run waypoint migrate`):
// each test copies the real migrations into a directory of its own, so the real lock is never touched.
[Collection(CatalogCollection.Name)]
public sealed class SeedMigratorTests : IDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = Path.Combine(Path.GetTempPath(), $"seed-migrator-{Guid.NewGuid():N}");
  private readonly MigrateOptions _options;

  public SeedMigratorTests() {
    var migrations = Path.Combine(_directory, "migrations");
    _ = Directory.CreateDirectory(migrations);
    foreach (var file in Directory.EnumerateFiles(RealMigrationsDirectory(), "*.sql")) {
      File.Copy(file, Path.Combine(migrations, Path.GetFileName(file)));
    }
    _options = new() {
      MigrationsDirectory = migrations,
      SeedsDirectory = Path.Combine(_directory, "seeds"),
    };
  }

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private static string RealMigrationsDirectory()
    => Path.Combine(RepositoryRoot.Find(), "JoshOlson.Database", "migrations");

  private async Task<MigrateResult> MigrateAsync(bool relock = false) {
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await connection.OpenAsync(_cancellationToken);
    return await SeedMigrator.MigrateAsync(connection, _options with { Relock = relock }, _cancellationToken);
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
      Version = current.Version - 1,
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
  public async Task MigrateAsync_Relock_StillRefusesWhenTheDatabaseLacksAMigration() {
    _ = await MigrateAsync();
    await File.WriteAllTextAsync(Path.Combine(_options.MigrationsDirectory, "V999__not_applied.sql"), "SELECT 1;\n", _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => MigrateAsync(relock: true));

    Assert.Contains("V999__not_applied.sql", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MigrateAsync_MigrationTheDatabaseDoesNotHave_WritesNoLock() {
    await File.WriteAllTextAsync(Path.Combine(_options.MigrationsDirectory, "V999__not_applied.sql"), "SELECT 1;\n", _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => MigrateAsync());

    Assert.Contains("V999__not_applied.sql", exception.Message, StringComparison.Ordinal);
    Assert.False(File.Exists(Path.Combine(_options.MigrationsDirectory, SeedMigrator.LockFileName)));
  }

  [Fact]
  public async Task MigrateAsync_SeedLayerPresent_RefusesBecauseLayersCannotMoveYet() {
    _ = Directory.CreateDirectory(_options.SeedsDirectory);
    const string Layer = """
      description: A layer
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1 }
      """;
    await File.WriteAllTextAsync(Path.Combine(_options.SeedsDirectory, "foo.yaml"), Layer, _cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => MigrateAsync());

    Assert.Contains("1 layer(s)", exception.Message, StringComparison.Ordinal);
    Assert.False(File.Exists(Path.Combine(_options.MigrationsDirectory, SeedMigrator.LockFileName)));
  }
}
