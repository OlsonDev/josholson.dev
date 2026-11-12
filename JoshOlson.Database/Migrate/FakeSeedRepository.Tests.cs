using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.IO;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Migrate;

// A small repository of its own for a test of moving layers: a schema, a lock for it, layers written against it, and
// migrations added one at a time, with `SeedMigrator.MigrateAsync` run over it. Its files are written by this class with
// fixed text, so every test that writes the same migrations gets the same schema templates, which are built the first time
// and found there by all the others (a name is a hash of the files; `EnsureTemplateAsync` makes concurrent callers wait
// for one build). They stay on the server for the next run, and `seed:clean` removes the ones the files no longer name.
internal sealed class FakeSeedRepository : IDisposable {
  private readonly CancellationToken _cancellationToken;
  private readonly string _directory = TempDirectory.Create("fake-seeds");

  public FakeSeedRepository(CancellationToken cancellationToken) {
    _cancellationToken = cancellationToken;
    var migrations = Path.Combine(_directory, "migrations");
    _ = Directory.CreateDirectory(migrations);
    _ = Directory.CreateDirectory(Path.Combine(_directory, "seeds"));
    var paths = SeedPaths.ForRepository();
    var databases = new ServerDatabases();
    Options = new() {
      MigrationsDirectory = migrations,
      SeedsDirectory = Path.Combine(_directory, "seeds"),
      Template = new(SchemaTemplateOptions.ForRepository(paths) with { MigrationsDirectory = migrations }, new(paths.Root), databases),
      Databases = databases,
      Clock = SystemClock.Instance,
      // Used when the lock is not behind the migrations and there is nothing to move: the command checks the layers
      // build, and these tests are about the move, so they take that as given.
      LayerCheck = (_, _) => Task.FromResult<List<string>>([]),
    };
  }

  public MigrateOptions Options { get; private set; }

  // The base (`origin/main` in real life) as a test says it is, from this point on: what the source returns, any warnings
  // it has, and the ref `--base` was given if any.
  public void UseBase(SeedSnapshot? snapshot, List<string>? warnings = null, string? explicitBase = null)
    => Options = Options with {
      Snapshots = new FakeSnapshotSource(new(snapshot, warnings ?? [])),
      Base = explicitBase,
    };

  // The lock and layers as they are right now, which is what main would have at the point a branch is made from it.
  public async Task<SeedSnapshot> SnapshotNowAsync() {
    var layers = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in LayerLoader.Files(Options.SeedsDirectory)) {
      layers[Path.GetFileNameWithoutExtension(path)] = await ReadAsync(path);
    }
    return new("origin/main at abc1234 (2026-11-12)", "abc1234def5678", await ReadAsync(LockFile), layers);
  }

  public string LockFile
    => Path.Combine(Options.MigrationsDirectory, SeedMigrator.LockFileName);

  public string Migration(string file)
    => Path.Combine(Options.MigrationsDirectory, file);

  public string Layer(string name)
    => Path.Combine(Options.SeedsDirectory, name + ".yaml");

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  public async Task WriteAsync(string path, string text)
    => await File.WriteAllTextAsync(path, text, _cancellationToken);

  public async Task<string> ReadAsync(string path)
    => await File.ReadAllTextAsync(path, _cancellationToken);

  public async Task<MigrateResult> MigrateAsync() {
    await using var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await owner.OpenAsync(_cancellationToken);
    return await SeedMigrator.MigrateAsync(owner, Options, _cancellationToken);
  }

  // V001: `foo.bar` with an identity key and a name.
  public async Task WriteBarMigrationAsync()
    => await WriteAsync(
      Migration("V001__bar.sql"),
      """
      CREATE SCHEMA foo;
      CREATE TABLE foo.bar (
        bar_id INT GENERATED ALWAYS AS IDENTITY,
        name TEXT NOT NULL,
        CONSTRAINT pk_bar PRIMARY KEY (bar_id)
      );

      """);

  // V001: `foo.kind`, which `foo.bar` points at, and a repeatable that fills it.
  public async Task WriteKindMigrationAsync() {
    await WriteAsync(
      Migration("V001__kind.sql"),
      """
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
  }

  // V002: a column that is not null and has a default, and a repeatable that fills a column the first version does not
  // have, so it cannot apply there and the reference row is missing until the migrations have run.
  public async Task WriteKindNoteMigrationAsync() {
    await WriteAsync(Migration("V002__note.sql"), "ALTER TABLE foo.kind ADD COLUMN note TEXT NOT NULL DEFAULT 'n';\n");
    await WriteAsync(
      Migration("R__kind.sql"),
      "INSERT INTO foo.kind (kind_id, label, note) VALUES (1, 'A', 'z') ON CONFLICT (kind_id) DO UPDATE SET note = excluded.note;\n");
  }

  public async Task WriteNoteMigrationAsync()
    => await WriteAsync(Migration("V002__note.sql"), "ALTER TABLE foo.bar ADD COLUMN note TEXT NOT NULL DEFAULT 'x';\n");

  // A parent and a child layer written against version 1 of the bar migration.
  public async Task WriteLayersAsync() {
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

  // The lock is made first, which the layers are read against; then the layers are written and the run is repeated, which
  // finds them and records them.
  public async Task<MigrateResult> MigrateWithLayersAtVersionOneAsync() {
    await WriteBarMigrationAsync();
    _ = await MigrateAsync();
    await WriteLayersAsync();
    return await MigrateAsync();
  }

  // V002 as an edit of the note migration would write it: the same column with another default.
  public const string NoteDefaultingToY = "ALTER TABLE foo.bar ADD COLUMN note TEXT NOT NULL DEFAULT 'y';\n";

  // Main has the layers at version 1; the branch adds V002 and moves forward from the working tree (no base is needed for
  // that). Returns main's snapshot, taken before the branch's migration.
  public async Task<SeedSnapshot> BranchWithAMigrationAsync() {
    _ = await MigrateWithLayersAtVersionOneAsync();
    var main = await SnapshotNowAsync();
    await WriteNoteMigrationAsync();
    _ = await MigrateAsync();
    return main;
  }

  // A layer the branch's author wrote (the lock has never recorded it), valid at version 2.
  public async Task WriteAuthoredLayerAsync(string note)
    => await WriteAsync(
      Layer("extra"),
      $$"""
      description: Extra
      extends: base
      tables:
        foo.bar:
          insert:
            - { bar_id: 1001, name: Extra, note: {{note}} }

      """);

  public async Task<List<LockedLayer>?> RecordedLayersAsync()
    => SchemaLockParser.Parse(await ReadAsync(LockFile)).Layers;

  public async Task<LockedLayer> ChecksumOfAsync(string layer)
    => new(layer, LayerChecksum.Of(await File.ReadAllBytesAsync(Layer(layer), _cancellationToken)));
}
