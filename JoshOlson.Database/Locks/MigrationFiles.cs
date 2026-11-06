using System.Globalization;
using System.Security.Cryptography;

namespace JoshOlson.Database.Locks;

public static class MigrationFiles {
  // The versioned migrations (`V<number>__<name>.sql`) in the directory, by version. Repeatable migrations
  // (`R__*.sql`) and any other file, the lock included, are left out. The checksum covers the raw bytes: the
  // repository keeps every file LF-terminated UTF-8 (`.gitattributes`), so nothing needs normalizing, and a
  // normalization would have to guess which whitespace is significant.
  public static async Task<List<MigrationFile>> LoadAsync(string directory, CancellationToken cancellationToken) {
    var migrations = new List<MigrationFile>();
    foreach (var path in Directory.EnumerateFiles(directory, "*.sql").Order(StringComparer.Ordinal)) {
      var file = Path.GetFileName(path);
      if (!TryParseVersion(file, out var version)) {
        continue;
      }
      var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
      migrations.Add(new() {
        Version = version,
        File = file,
        Checksum = Convert.ToHexStringLower(SHA256.HashData(bytes)),
      });
    }
    migrations.Sort((left, right) => left.Version.CompareTo(right.Version));
    for (var i = 1; i < migrations.Count; i++) {
      if (migrations[i].Version == migrations[i - 1].Version) {
        throw new SeedFormatException(FormattableString.Invariant(
          $"{migrations[i - 1].File} and {migrations[i].File} are both version {migrations[i].Version}. Two branches claimed the same number: rename yours to the next free one."));
      }
    }
    return migrations;
  }

  // `V042__foo.sql` is version 42. The part after `__` is the description, and must not be empty.
  public static bool TryParseVersion(string file, out int version) {
    version = 0;
    if (!file.StartsWith('V') || !file.EndsWith(".sql", StringComparison.Ordinal)) {
      return false;
    }
    var separator = file.IndexOf("__", StringComparison.Ordinal);
    return separator > 1
      && file.Length > separator + "__".Length + ".sql".Length
      && int.TryParse(file.AsSpan(1, separator - 1), NumberStyles.None, CultureInfo.InvariantCulture, out version);
  }
}
