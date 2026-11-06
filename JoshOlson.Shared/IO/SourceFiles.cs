namespace JoshOlson.Shared.IO;

public static class SourceFiles {
  // Every file matching the pattern under the directories, in ordinal path order, skipping build output
  // (any `bin` or `obj` folder). A directory that does not exist is an error, so a renamed project is noticed.
  public static List<string> Enumerate(IEnumerable<string> directories, string pattern) {
    var files = new List<string>();
    foreach (var directory in directories) {
      foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)) {
        if (!path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) {
          files.Add(path);
        }
      }
    }
    files.Sort(StringComparer.Ordinal);
    return files;
  }
}
