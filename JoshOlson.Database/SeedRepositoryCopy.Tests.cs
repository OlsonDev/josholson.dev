using JoshOlson.Shared.IO;

namespace JoshOlson.Database;

// A copy of what the seed tool reads (the migrations with the lock, the layers, and the scripts that are part of a
// template's name) in a directory of its own, so a test can change a layer or a migration without touching the real ones
// and the templates it builds have the real names. Dispose deletes it.
public sealed class SeedRepositoryCopy : IDisposable {
  public SeedRepositoryCopy(string name) {
    var real = SeedPaths.ForRepository();
    Root = TempDirectory.Create(name);
    Paths = new(Root);
    _ = Directory.CreateDirectory(Paths.Migrations);
    foreach (var file in Directory.EnumerateFiles(real.Migrations)) {
      File.Copy(file, Path.Combine(Paths.Migrations, Path.GetFileName(file)));
    }
    _ = Directory.CreateDirectory(Paths.Seeds);
    foreach (var file in Directory.EnumerateFiles(real.Seeds, "*.yaml")) {
      File.Copy(file, Path.Combine(Paths.Seeds, Path.GetFileName(file)));
    }
    foreach (var script in new[] { Paths.ProvisioningScript, Paths.SeedSchemaScript }) {
      _ = Directory.CreateDirectory(Path.GetDirectoryName(script)!);
    }
    File.Copy(real.ProvisioningScript, Paths.ProvisioningScript);
    File.Copy(real.SeedSchemaScript, Paths.SeedSchemaScript);
  }

  public string Root { get; }

  public SeedPaths Paths { get; }

  public string Layer(string name)
    => Path.Combine(Paths.Seeds, name + ".yaml");

  public void Dispose()
    => Directory.Delete(Root, recursive: true);
}
