using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.IO;

namespace JoshOlson.Database;

// A repository of a test's own: a temporary directory laid out as `SeedPaths` expects (the migrations with the lock
// beside them, the seed layers, and the two scripts that finish a template), so a test can change a migration or a
// layer without touching the real ones. The scripts are always copies of the real ones: a template's name is a hash of the
// migrations and both scripts, so a workspace with the real migrations names the real templates, and one with fixed
// migrations of its own names the same templates on every run. Dispose deletes the directory.
public sealed class SeedWorkspace : IDisposable {
  private SeedWorkspace(string name) {
    var real = SeedPaths.ForRepository();
    Root = TempDirectory.Create(name);
    Paths = new(Root);
    _ = Directory.CreateDirectory(Paths.Migrations);
    _ = Directory.CreateDirectory(Paths.Seeds);
    foreach (var script in new[] { Paths.ProvisioningScript, Paths.SeedSchemaScript }) {
      _ = Directory.CreateDirectory(Path.GetDirectoryName(script)!);
    }
    File.Copy(real.ProvisioningScript, Paths.ProvisioningScript);
    File.Copy(real.SeedSchemaScript, Paths.SeedSchemaScript);
    Template = new(SchemaTemplateOptions.ForRepository(Paths), SeedTestServer.Tools, SeedTestServer.Databases);
  }

  public string Root { get; }

  public SeedPaths Paths { get; }

  // The schema template for the workspace's migrations and scripts, whatever they are when it is asked.
  public SchemaTemplate Template { get; }

  public string LockFile
    => Paths.LockFile;

  // What `seeds:migrate` needs for the workspace; a test adds the rest with `with`.
  public MigrateOptions MigrateOptions
    => MigrateOptions.For(Paths, Template, SeedTestServer.Databases, SeedTestServer.Clock);

  // No migrations and no layers: a test writes its own.
  public static SeedWorkspace Empty(string name)
    => new(name);

  // The real migrations, with the real lock and the real layers unless a test says otherwise.
  public static SeedWorkspace CopyOfRepository(string name, bool withLock = true, bool withLayers = true) {
    var real = SeedPaths.ForRepository();
    var workspace = new SeedWorkspace(name);
    foreach (var file in Directory.EnumerateFiles(real.Migrations, "*.sql")) {
      File.Copy(file, workspace.Migration(Path.GetFileName(file)));
    }
    if (withLock) {
      File.Copy(real.LockFile, workspace.LockFile);
    }
    if (withLayers) {
      workspace.CopyRepositoryLayers();
    }
    return workspace;
  }

  // The real layers, for a workspace made without them that a test wants them in after all.
  public void CopyRepositoryLayers() {
    foreach (var file in LayerLoader.Files(SeedPaths.ForRepository().Seeds)) {
      File.Copy(file, Path.Combine(Paths.Seeds, Path.GetFileName(file)));
    }
  }

  public string Migration(string file)
    => Path.Combine(Paths.Migrations, file);

  public string Layer(string name)
    => Paths.Layer(name);

  // Writes a migration by its file name (`V002__foo.sql`), over the one of that name if there is one.
  public async Task WriteMigrationAsync(string file, string text, CancellationToken cancellationToken)
    => await File.WriteAllTextAsync(Migration(file), text, cancellationToken);

  // The same for a constructor, which cannot await.
  public void WriteMigration(string file, string text)
    => File.WriteAllText(Migration(file), text);

  // Writes a layer by its name, over the one of that name if there is one.
  public async Task WriteLayerAsync(string name, string text, CancellationToken cancellationToken)
    => await File.WriteAllTextAsync(Layer(name), text, cancellationToken);

  public async Task<string> ReadLayerAsync(string name, CancellationToken cancellationToken)
    => await File.ReadAllTextAsync(Layer(name), cancellationToken);

  public async Task<string> ReadLockAsync(CancellationToken cancellationToken)
    => await File.ReadAllTextAsync(LockFile, cancellationToken);

  // The tool over the workspace's own lock and layers, as they are now.
  public async Task<SeedTool> OpenToolAsync(CancellationToken cancellationToken)
    => await SeedTool.OpenAsync(
      Paths,
      SeedTestServer.Databases,
      SeedTestServer.Tools,
      SeedTestServer.Clock,
      cancellationToken);

  // The migrations, the lock, and the layers as they are now: each file's path from the root (with `/`) and its text.
  public async Task<Dictionary<string, string>> SnapshotFilesAsync(CancellationToken cancellationToken) {
    var files = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in Directory.EnumerateFiles(Paths.Migrations).Concat(LayerLoader.Files(Paths.Seeds))) {
      var relative = Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');
      files[relative] = await File.ReadAllTextAsync(path, cancellationToken);
    }
    return files;
  }

  // Writes such a set of files into the workspace, over whatever is there. A file the set does not have is left alone.
  public async Task StampAsync(IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken) {
    foreach (var (relative, text) in files) {
      var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
      _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      await File.WriteAllTextAsync(path, text, cancellationToken);
    }
  }

  public void Dispose()
    => Directory.Delete(Root, recursive: true);
}
