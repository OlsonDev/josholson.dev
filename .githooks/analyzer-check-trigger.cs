static class AnalyzerCheckTrigger {
  // The set of analyzer rules changes only when a package version changes, and a rule's line lives in .editorconfig.
  // So `mise run analyzers:check` runs only when a commit stages one of these.
  public static bool IsNeeded(IEnumerable<string> stagedPaths)
    => stagedPaths.Any(path => Path.GetFileName(path) is ".editorconfig" or "Directory.Packages.props" or "Directory.Build.props"
      || path.EndsWith(".csproj", StringComparison.Ordinal)
      || path is "scripts/analyzers.cs");
}
