#!/usr/bin/env dotnet
#:include branch-name-validator.cs
#:include analyzer-check-trigger.cs

using System.Diagnostics;

var gitDir = Git("rev-parse --git-dir");
// Branch names don't matter while a rebase replays commits
if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return 0;

var (success, error) = BranchNameValidator.ValidateBranchName(Git("branch --show-current"));
if (!success) {
  Console.Error.WriteLine(error);
  return 1;
}

// Only an upgrade (or an edit to .editorconfig) can leave an analyzer rule undecided, so check just then (ADR 0030)
if (AnalyzerCheckTrigger.IsNeeded(Git("diff --cached --name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries))) {
  using var check = Process.Start(new ProcessStartInfo("dotnet", ["run", "scripts/analyzers.cs", "--", "check"]) { UseShellExecute = false })!;
  check.WaitForExit();
  if (check.ExitCode != 0) {
    Console.Error.WriteLine("Every analyzer rule needs a severity in .editorconfig. Decide the ones listed above, or run `mise run analyzers:add`.");
    return 1;
  }
}
return 0;

static string Git(string arguments) {
  var startInfo = new ProcessStartInfo("git", arguments) { RedirectStandardOutput = true, UseShellExecute = false };
  using var process = Process.Start(startInfo)!;
  var output = process.StandardOutput.ReadToEnd().Trim();
  process.WaitForExit();
  return output;
}
