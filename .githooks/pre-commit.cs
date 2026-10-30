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

var staged = Git("diff --cached --name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries);

// Only an upgrade (or an edit to .editorconfig) can leave an analyzer rule undecided, so check just then (ADR 0030)
if (AnalyzerCheckTrigger.IsNeeded(staged)
    && !Passes("Every analyzer rule needs a severity in .editorconfig. Decide the ones listed above, or run `mise run analyzers:add`.",
      "scripts/analyzers.cs", "--", "check")) {
  return 1;
}

// The scripts and hooks are checked by building them
if (AnalyzerCheckTrigger.IsScriptCheckNeeded(staged)
    && !Passes("A script fails the style rules or analyzers. Fix the ones listed above; `mise run scripts:check` reruns the check.",
      "scripts/check-scripts.cs")) {
  return 1;
}
return 0;

static bool Passes(string failure, params string[] arguments) {
  var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
  startInfo.ArgumentList.Add("run");
  foreach (var argument in arguments) {
    startInfo.ArgumentList.Add(argument);
  }
  using var process = Process.Start(startInfo)!;
  process.WaitForExit();
  if (process.ExitCode is 0) return true;
  Console.Error.WriteLine(failure);
  return false;
}

static string Git(string arguments) {
  var startInfo = new ProcessStartInfo("git", arguments) {
    RedirectStandardOutput = true,
    UseShellExecute = false,
  };
  using var process = Process.Start(startInfo)!;
  var output = process.StandardOutput.ReadToEnd().Trim();
  process.WaitForExit();
  return output;
}
