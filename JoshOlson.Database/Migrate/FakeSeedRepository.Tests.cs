using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;

namespace JoshOlson.Database.Migrate;

// A small repository of its own for a test of moving layers: a schema, a lock for it, layers written against it, and
// migrations added one at a time, with `SeedMigrator.MigrateAsync` run over it. It is an empty `SeedWorkspace` that
// `FooSchema`'s fixed text is written into, so every test that writes the same migrations gets the same schema
// templates, which are built the first time and found there by all the others (a name is a hash of the files;
// `EnsureTemplateAsync` makes concurrent callers wait for one build). They stay on the server for the next run, and
// `seed:clean` removes the ones the files no longer name.
internal sealed class FakeSeedRepository : IDisposable {
  private readonly CancellationToken _cancellationToken;

  public FakeSeedRepository(CancellationToken cancellationToken) {
    _cancellationToken = cancellationToken;
    Options = Workspace.MigrateOptions with {
      // Used when the lock is not behind the migrations and there is nothing to move: the command checks the layers
      // build, and these tests are about the move, so they take that as given.
      LayerCheck = (_, _) => Task.FromResult<List<string>>([]),
    };
  }

  public SeedWorkspace Workspace { get; } = SeedWorkspace.Empty("fake-seeds");

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
      var name = Path.GetFileNameWithoutExtension(path);
      layers[name] = await ReadLayerAsync(name);
    }
    return new("origin/main at abc1234 (2026-11-12)", "abc1234def5678", await ReadLockAsync(), layers);
  }

  public string Migration(string file)
    => Workspace.Migration(file);

  public void Dispose()
    => Workspace.Dispose();

  public async Task WriteMigrationAsync(string file, string text)
    => await Workspace.WriteMigrationAsync(file, text, _cancellationToken);

  public async Task WriteLayerAsync(string name, string text)
    => await Workspace.WriteLayerAsync(name, text, _cancellationToken);

  public async Task<string> ReadLayerAsync(string name)
    => await Workspace.ReadLayerAsync(name, _cancellationToken);

  public async Task<string> ReadLockAsync()
    => await Workspace.ReadLockAsync(_cancellationToken);

  public async Task<MigrateResult> MigrateAsync() {
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    return await SeedMigrator.MigrateAsync(owner, Options, _cancellationToken);
  }

  public async Task WriteBarMigrationAsync()
    => await WriteMigrationAsync(FooSchema.BarFile, FooSchema.Bar);

  public async Task WriteKindMigrationAsync() {
    await WriteMigrationAsync(FooSchema.KindFile, FooSchema.Kind);
    await WriteMigrationAsync(FooSchema.KindRepeatableFile, FooSchema.KindRepeatable);
  }

  public async Task WriteKindNoteMigrationAsync() {
    await WriteMigrationAsync(FooSchema.NoteFile, FooSchema.KindNote);
    await WriteMigrationAsync(FooSchema.KindRepeatableFile, FooSchema.KindNoteRepeatable);
  }

  public async Task WriteNoteMigrationAsync(string @default = "x")
    => await WriteMigrationAsync(FooSchema.NoteFile, FooSchema.Note(@default));

  // A parent and a child layer written against version 1 of the bar migration.
  public async Task WriteLayersAsync() {
    await WriteLayerAsync("base", FooSchema.BaseLayer());
    await WriteLayerAsync("child", FooSchema.ChildLayer());
  }

  // The lock is made first, which the layers are read against; then the layers are written and the run is repeated, which
  // finds them and records them.
  public async Task<MigrateResult> MigrateWithLayersAtVersionOneAsync() {
    await WriteBarMigrationAsync();
    _ = await MigrateAsync();
    await WriteLayersAsync();
    return await MigrateAsync();
  }

  // Main has the layers at version 1; the branch adds V002 and moves forward from the working tree (no base is needed for
  // that). Returns main's snapshot, taken before the branch's migration.
  public async Task<SeedSnapshot> BranchWithAMigrationAsync() {
    _ = await MigrateWithLayersAtVersionOneAsync();
    var main = await SnapshotNowAsync();
    await WriteNoteMigrationAsync();
    _ = await MigrateAsync();
    return main;
  }

  public async Task<List<LockedLayer>?> RecordedLayersAsync()
    => SchemaLockParser.Parse(await ReadLockAsync()).Layers;

  public async Task<LockedLayer> ChecksumOfAsync(string layer)
    => new(layer, LayerChecksum.Of(await File.ReadAllBytesAsync(Workspace.Layer(layer), _cancellationToken)));
}
