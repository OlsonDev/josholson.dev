using JoshOlson.Database.Baking;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.IO;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Migrate;

// Each test makes a small repository of its own (a schema, a lock for it, layers written against it), adds a migration, and
// runs `seeds:migrate` over it, so what a migration does to a layer is seen from the files. The templates it builds are
// named for its own files, and dropped when it ends.
[Collection(TemplateCollection.Name)]
public sealed class LayerMoverTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _directory = TempDirectory.Create("layer-mover");
  private readonly ServerDatabases _databases = new();
  private readonly HashSet<string> _templates = [];
  private readonly MigrateOptions _options;
  private readonly SchemaTemplate _template;

  public LayerMoverTests() {
    var migrations = Path.Combine(_directory, "migrations");
    _ = Directory.CreateDirectory(migrations);
    _ = Directory.CreateDirectory(Path.Combine(_directory, "seeds"));
    var paths = SeedPaths.ForRepository();
    _template = new(SchemaTemplateOptions.ForRepository(paths) with { MigrationsDirectory = migrations }, new(paths.Root), _databases);
    _options = new() {
      MigrationsDirectory = migrations,
      SeedsDirectory = Path.Combine(_directory, "seeds"),
      Template = _template,
      Databases = _databases,
      Clock = SystemClock.Instance,
      // Used when the lock is not behind the migrations and there is nothing to move: the command checks the layers
      // build, and these tests are about the move, so they take that as given.
      LayerCheck = (_, _) => Task.FromResult<List<string>>([]),
    };
  }

  public ValueTask InitializeAsync()
    => ValueTask.CompletedTask;

  public async ValueTask DisposeAsync() {
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(CancellationToken.None);
    foreach (var name in _templates) {
      await _databases.DropAsync(owner, name, CancellationToken.None);
    }
    Directory.Delete(_directory, recursive: true);
  }

  private string Migration(string file)
    => Path.Combine(_options.MigrationsDirectory, file);

  private string Layer(string name)
    => Path.Combine(_options.SeedsDirectory, name + ".yaml");

  private string LockFile
    => Path.Combine(_options.MigrationsDirectory, SeedMigrator.LockFileName);

  private async Task WriteAsync(string path, string text)
    => await File.WriteAllTextAsync(path, text, _cancellationToken);

  private async Task<MigrateResult> MigrateAsync() {
    // Every version's template for the files as they are now, so each one built can be dropped at the end.
    foreach (var migration in await MigrationFiles.LoadAsync(_options.MigrationsDirectory, _cancellationToken)) {
      _ = _templates.Add(await _template.NameAsync(migration.Version, _cancellationToken));
    }
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(_cancellationToken);
    return await SeedMigrator.MigrateAsync(owner, _options, _cancellationToken);
  }

  // V001: foo.bar with an identity key and a name. The comment makes the template's name this test's own.
  private async Task WriteFirstMigrationAsync()
    => await WriteAsync(
      Migration("V001__bar.sql"),
      $"""
      -- {Guid.NewGuid():N}
      CREATE SCHEMA foo;
      CREATE TABLE foo.bar (
        bar_id INT GENERATED ALWAYS AS IDENTITY,
        name TEXT NOT NULL,
        CONSTRAINT pk_bar PRIMARY KEY (bar_id)
      );

      """);

  // A parent and a child layer written against version 1 (the lock is made first, which the layers are read against).
  private async Task WriteLayersAsync() {
    await WriteAsync(
      Layer("base"),
      """
      description: Base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1, name: One }
            - { bar_id: 2, name: Two }

      """);
    await WriteAsync(
      Layer("child"),
      """
      description: Child
      extends: base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1001, name: Three }
          update:
            - { bar_id: 1, name: Uno }

      """);
  }

  private async Task<MigrateResult> MigrateWithLayersAtVersionOneAsync() {
    await WriteFirstMigrationAsync();
    _ = await MigrateAsync();
    await WriteLayersAsync();
    return await MigrateAsync();
  }

  [Fact]
  public async Task MigrateAsync_NothingPending_LeavesTheLayersAlone() {
    var result = await MigrateWithLayersAtVersionOneAsync();

    Assert.Empty(result.Layers);
    Assert.Equal(1, SchemaLockParser.Parse(await File.ReadAllTextAsync(LockFile, _cancellationToken)).Version);
  }

  [Fact]
  public async Task MigrateAsync_ANotNullColumnWithADefault_IsNamedInEveryInsert() {
    _ = await MigrateWithLayersAtVersionOneAsync();
    await WriteAsync(Migration("V002__note.sql"), "ALTER TABLE foo.bar ADD COLUMN note TEXT NOT NULL DEFAULT 'x';\n");

    var result = await MigrateAsync();

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
      await File.ReadAllTextAsync(Layer("base"), _cancellationToken));
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
      await File.ReadAllTextAsync(Layer("child"), _cancellationToken));
    Assert.Equal(2, SchemaLockParser.Parse(await File.ReadAllTextAsync(LockFile, _cancellationToken)).Version);
  }

  [Fact]
  public async Task MigrateAsync_AMigrationThatChangesData_ChangesTheLayers() {
    _ = await MigrateWithLayersAtVersionOneAsync();
    await WriteAsync(Migration("V002__upper.sql"), "UPDATE foo.bar SET name = upper(name);\n");

    _ = await MigrateAsync();

    Assert.Contains("{ bar_id: 1, name: ONE }", await File.ReadAllTextAsync(Layer("base"), _cancellationToken), StringComparison.Ordinal);
    var child = await File.ReadAllTextAsync(Layer("child"), _cancellationToken);
    Assert.Contains("{ bar_id: 1001, name: THREE }", child, StringComparison.Ordinal);
    Assert.Contains("{ bar_id: 1, name: UNO }", child, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MigrateAsync_AMigrationThatLeavesALayerAsItWas_DoesNotRewriteTheFile() {
    _ = await MigrateWithLayersAtVersionOneAsync();
    var before = await File.ReadAllTextAsync(Layer("base"), _cancellationToken);
    await WriteAsync(Migration("V002__baz.sql"), "CREATE TABLE foo.baz (baz_id INT NOT NULL, CONSTRAINT pk_baz PRIMARY KEY (baz_id));\n");

    var result = await MigrateAsync();

    Assert.Equal([("base", false), ("child", false)], result.Layers);
    Assert.Equal(before, await File.ReadAllTextAsync(Layer("base"), _cancellationToken));
    Assert.Equal(2, result.Version);
  }

  [Fact]
  public async Task MigrateAsync_AMigrationThatFailsOnALayersRows_NamesTheLayerAndWritesNothing() {
    _ = await MigrateWithLayersAtVersionOneAsync();
    var lockBefore = await File.ReadAllTextAsync(LockFile, _cancellationToken);
    var baseBefore = await File.ReadAllTextAsync(Layer("base"), _cancellationToken);
    await WriteAsync(Migration("V002__check.sql"), "ALTER TABLE foo.bar ADD CONSTRAINT ck_bar_name CHECK (name <> 'Two');\n");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(MigrateAsync);

    Assert.Contains("base: the migrations failed on its rows", exception.Message, StringComparison.Ordinal);
    Assert.DoesNotContain("child:", exception.Message, StringComparison.Ordinal);
    Assert.Equal(lockBefore, await File.ReadAllTextAsync(LockFile, _cancellationToken));
    Assert.Equal(baseBefore, await File.ReadAllTextAsync(Layer("base"), _cancellationToken));
  }

  [Fact]
  public async Task MigrateAsync_RowThatPointsAtReferenceDataTheOldSchemaCouldNotHave_IsMovedAndCheckedAtTheNewestVersion() {
    await WriteAsync(
      Migration("V001__kind.sql"),
      $"""
      -- {Guid.NewGuid():N}
      CREATE SCHEMA foo;
      CREATE TABLE foo.kind (kind_id INT NOT NULL, label TEXT NOT NULL, CONSTRAINT pk_kind PRIMARY KEY (kind_id));
      CREATE TABLE foo.bar (
        bar_id INT GENERATED ALWAYS AS IDENTITY,
        kind_id INT NOT NULL,
        CONSTRAINT pk_bar PRIMARY KEY (bar_id),
        CONSTRAINT fk_bar_kind FOREIGN KEY (kind_id) REFERENCES foo.kind (kind_id)
      );

      """);
    await WriteAsync(Migration("R__kind.sql"), "INSERT INTO foo.kind (kind_id, label) VALUES (1, 'A') ON CONFLICT (kind_id) DO NOTHING;\n");
    _ = await MigrateAsync();
    await WriteAsync(
      Layer("base"),
      """
      description: Base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1, kind_id: 1 }

      """);
    // The repeatable now fills a column the first version does not have, so it cannot apply there and the reference row is
    // missing until the migrations have run.
    await WriteAsync(Migration("V002__note.sql"), "ALTER TABLE foo.kind ADD COLUMN note TEXT NOT NULL DEFAULT 'n';\n");
    await WriteAsync(
      Migration("R__kind.sql"),
      "INSERT INTO foo.kind (kind_id, label, note) VALUES (1, 'A', 'z') ON CONFLICT (kind_id) DO UPDATE SET note = excluded.note;\n");

    var result = await MigrateAsync();

    Assert.Equal([("base", false)], result.Layers);
    Assert.Equal(2, result.Version);
  }

  [Fact]
  public async Task MigrateAsync_RowThatPointsAtReferenceDataTheNewestVersionDoesNotHave_NamesTheLayerAndTheSkippedRepeatable() {
    await WriteAsync(
      Migration("V001__kind.sql"),
      $"""
      -- {Guid.NewGuid():N}
      CREATE SCHEMA foo;
      CREATE TABLE foo.kind (kind_id INT NOT NULL, label TEXT NOT NULL, CONSTRAINT pk_kind PRIMARY KEY (kind_id));
      CREATE TABLE foo.bar (
        bar_id INT GENERATED ALWAYS AS IDENTITY,
        kind_id INT NOT NULL,
        CONSTRAINT pk_bar PRIMARY KEY (bar_id),
        CONSTRAINT fk_bar_kind FOREIGN KEY (kind_id) REFERENCES foo.kind (kind_id)
      );

      """);
    await WriteAsync(Migration("R__kind.sql"), "INSERT INTO foo.kind (kind_id, label) VALUES (1, 'A') ON CONFLICT (kind_id) DO NOTHING;\n");
    _ = await MigrateAsync();
    await WriteAsync(
      Layer("base"),
      """
      description: Base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1, kind_id: 2 }

      """);
    await WriteAsync(Migration("V002__note.sql"), "ALTER TABLE foo.kind ADD COLUMN note TEXT NOT NULL DEFAULT 'n';\n");
    await WriteAsync(
      Migration("R__kind.sql"),
      "INSERT INTO foo.kind (kind_id, label, note) VALUES (1, 'A', 'z') ON CONFLICT (kind_id) DO UPDATE SET note = excluded.note;\n");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(MigrateAsync);

    Assert.Contains("base: does not build at the newest version", exception.Message, StringComparison.Ordinal);
    Assert.Contains("R__kind.sql", exception.Message, StringComparison.Ordinal);
  }
}
