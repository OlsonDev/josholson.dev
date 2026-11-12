using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;

namespace JoshOlson.Database.Migrate;

// Which layer files are the tool's own: the ones that still hash to what `seeds:migrate` recorded for them in the lock.
// Those it may write again; the rest were written or edited by somebody and are only ever checked, never replaced. A lock
// that says nothing about layers (`SchemaLock.Layers` is null: from before they were recorded) takes every layer to be the
// tool's, which is how the first run records them.
public static class GeneratedLayers {
  public static async Task<HashSet<string>> FindAsync(string seedsDirectory, SchemaLock? schemaLock, CancellationToken cancellationToken) {
    var generated = new HashSet<string>(StringComparer.Ordinal);
    foreach (var path in LayerLoader.Files(seedsDirectory)) {
      var name = Path.GetFileNameWithoutExtension(path);
      if (schemaLock?.Layers is not { } recorded) {
        _ = generated.Add(name);
        continue;
      }
      var known = recorded.Find(layer => string.Equals(layer.Name, name, StringComparison.Ordinal));
      if (known is not null && string.Equals(known.Checksum, LayerChecksum.Of(await File.ReadAllBytesAsync(path, cancellationToken)), StringComparison.Ordinal)) {
        _ = generated.Add(name);
      }
    }
    return generated;
  }
}
