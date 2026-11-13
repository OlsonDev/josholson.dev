namespace JoshOlson.Database.Migrate;

// A starting point for a test of `seeds:migrate` (see `FooScenarios`): the files of a workspace after the last run that
// made it (the migrations, the lock, and the layers, keyed as `SeedWorkspace.SnapshotFilesAsync` keys them), and what
// that run returned. `Result.Path` names the directory it was built in, which is gone.
internal sealed record FooScenario(IReadOnlyDictionary<string, string> Files, MigrateResult Result) {
  private static readonly SeedPaths _Paths = new("");

  // The lock as the scenario has it.
  public string LockText
    => Files[Key(_Paths.LockFile)];

  // The scenario as the base of a branch (`origin/main` as a test says it is): its lock and its layers by name.
  public SeedSnapshot AsSnapshot() {
    var seeds = Key(_Paths.Seeds) + "/";
    var layers = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (file, text) in Files) {
      if (file.StartsWith(seeds, StringComparison.Ordinal)) {
        layers[Path.GetFileNameWithoutExtension(file)] = text;
      }
    }
    return new("origin/main at abc1234 (2026-11-12)", "abc1234def5678", LockText, layers);
  }

  private static string Key(string path)
    => path.Replace(Path.DirectorySeparatorChar, '/');
}
