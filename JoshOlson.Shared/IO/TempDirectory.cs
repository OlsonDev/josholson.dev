namespace JoshOlson.Shared.IO;

// Working directories for tests and scripts, under `.tmp/` at the repository root (gitignored) instead of the operating
// system's temp directory. Whether the OS ever clears its temp directory depends on the OS, and a run that was killed
// leaves its directory behind either way. Here a leftover sits beside the code where it can be seen, and every `Create`
// first removes the ones old enough that no run can still be using them.
public static class TempDirectory {
  public const string Name = ".tmp";

  // A directory not touched for this long belongs to a run that is gone.
  public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

  // Makes `.tmp/<name>-<guid>` and returns its path. The caller deletes it when done.
  public static string Create(string name) {
    var root = Path.Combine(RepositoryRoot.Find(), Name);
    _ = Directory.CreateDirectory(root);
    Sweep(root, MaxAge);
    return Directory.CreateDirectory(Path.Combine(root, $"{name}-{Guid.NewGuid():N}")).FullName;
  }

  // Removes the directories under `root` that nothing has written to for `maxAge`. One that cannot be removed (a run
  // using it, a file held open) is left for the next sweep.
  public static int Sweep(string root, TimeSpan maxAge) {
    var swept = 0;
    foreach (var directory in new DirectoryInfo(root).EnumerateDirectories()) {
      if (DateTime.UtcNow - directory.LastWriteTimeUtc >= maxAge && TryDelete(directory)) {
        swept++;
      }
    }
    return swept;
  }

  private static bool TryDelete(DirectoryInfo directory) {
    try {
      directory.Delete(recursive: true);
      return true;
    } catch (IOException) {
      return false;
    } catch (UnauthorizedAccessException) {
      return false;
    }
  }
}
