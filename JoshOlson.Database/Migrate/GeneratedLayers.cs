using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;

namespace JoshOlson.Database.Migrate;

// Which layer files are the tool's own, so it may write them again, and which are somebody's, so it only ever checks them.
// A file is the tool's if either:
// - it still hashes to what `seeds:migrate` recorded for it in the lock: the tool wrote it and nobody has touched it since;
// - it is exactly the file the base has: whatever it was, it is already on the base (merged), and the base's tool owns it.
// A lock that says nothing about layers (`SchemaLock.Layers` is null: from before they were recorded) takes every layer to be
// the tool's, which is how the first run records them.
//
// The base is read only for a file the hash does not vouch for, so a run where every layer is recorded never asks for it.
public static class GeneratedLayers {
  public static async Task<HashSet<string>> FindAsync(
    string seedsDirectory,
    SchemaLock? schemaLock,
    Func<Task<SeedSnapshot?>> snapshot,
    CancellationToken cancellationToken
  ) {
    var generated = new HashSet<string>(StringComparer.Ordinal);
    foreach (var path in LayerLoader.Files(seedsDirectory)) {
      var name = Path.GetFileNameWithoutExtension(path);
      if (schemaLock?.Layers is not { } recorded || await IsOwnAsync(name, path, recorded, snapshot, cancellationToken)) {
        _ = generated.Add(name);
      }
    }
    return generated;
  }

  private static async Task<bool> IsOwnAsync(
    string name,
    string path,
    List<LockedLayer> recorded,
    Func<Task<SeedSnapshot?>> snapshot,
    CancellationToken cancellationToken
  ) {
    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
    var known = recorded.Find(layer => string.Equals(layer.Name, name, StringComparison.Ordinal));
    if (known is not null && string.Equals(known.Checksum, LayerChecksum.Of(bytes), StringComparison.Ordinal)) {
      return true;
    }
    return await snapshot() is { } found
      && found.Layers.TryGetValue(name, out var onBase)
      && string.Equals(onBase, await File.ReadAllTextAsync(path, cancellationToken), StringComparison.Ordinal);
  }
}
