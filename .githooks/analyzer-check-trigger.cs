internal static class AnalyzerCheckTrigger {
  // The set of analyzer rules changes only when a package version changes, and a rule's line lives in .editorconfig.
  // So `mise run analyzers:check` runs only when a commit stages one of these.
  public static bool IsNeeded(IEnumerable<string> stagedPaths)
    => stagedPaths.Any(path => Path.GetFileName(path) is ".editorconfig" or "Directory.Packages.props" or "Directory.Build.props"
                            || path.EndsWith(".csproj", StringComparison.Ordinal)
                            || path is "scripts/analyzers.cs");

  // The scripts are checked by building them, which a change to any script or to the settings they build with can break.
  public static bool IsScriptCheckNeeded(IEnumerable<string> stagedPaths)
    => stagedPaths.Any(path => Path.GetFileName(path) is ".editorconfig" or "Directory.Build.props"
                            || path.EndsWith(".cs", StringComparison.Ordinal)
                            && (path.StartsWith("scripts/", StringComparison.Ordinal)
                              || path.StartsWith(".githooks/", StringComparison.Ordinal)));
}
