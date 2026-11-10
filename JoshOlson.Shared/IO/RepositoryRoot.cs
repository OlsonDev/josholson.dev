namespace JoshOlson.Shared.IO;

public static class RepositoryRoot {
  private const string _Marker = "JoshOlson.slnx";

  // The directory that holds `JoshOlson.slnx`, found by walking up from where the process runs (a script runs from the
  // repository root; a test runs from its output directory, somewhere below it).
  public static string Find()
    => TryFind(Directory.GetCurrentDirectory())
    ?? TryFind(AppContext.BaseDirectory)
    ?? throw new DirectoryNotFoundException($"{_Marker} was not found above the working directory or the output directory.");

  // The same walk from a given directory, or null when no directory above it holds the solution.
  public static string? TryFind(string start) {
    var directory = new DirectoryInfo(start);
    while (directory is not null) {
      if (File.Exists(Path.Combine(directory.FullName, _Marker))) {
        return directory.FullName;
      }
      directory = directory.Parent;
    }
    return null;
  }
}
