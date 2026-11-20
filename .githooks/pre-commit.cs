#!/usr/bin/env dotnet
#:include branch-name-validator.cs
#:include analyzer-check-trigger.cs
#:include web-format-check.cs

using System.Diagnostics;

var gitDir = Git("rev-parse --git-dir");
// Branch names don't matter while a rebase replays commits
if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return 0;

if (BranchNameValidator.ValidateBranchName(Git("branch --show-current")) is { } error) {
  Console.Error.WriteLine(error);
  return 1;
}

var staged = Git("diff --cached --name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries);

// Only an upgrade (or an edit to .editorconfig) can leave an analyzer rule undecided, so check just then (ADR 0030)
if (AnalyzerCheckTrigger.IsNeeded(staged)
    && !Passes(
      "Every analyzer rule needs a severity in .editorconfig. Decide the ones listed above, or run `mise analyzers:add`.",
      "scripts/analyzers.cs",
      "--",
      "check")) {
  return 1;
}

// The scripts and hooks are checked by building them
if (AnalyzerCheckTrigger.IsScriptCheckNeeded(staged)
    && !Passes(
      "A script fails the style rules or analyzers. Fix the ones listed above; `mise scripts:check` reruns the check.",
      "scripts/check-scripts.cs")) {
  return 1;
}

// The web files are formatted, imports sorted included, by `bun fmt` (ADR 0045); what a commit stages must already be
var webSources = WebFormatCheck.Sources(Git("diff --cached --name-only --diff-filter=ACMR").Split('\n', StringSplitOptions.RemoveEmptyEntries));
if (webSources.Count > 0) {
  List<string> unformatted;
  try {
    unformatted = WebFormatCheck.Unformatted(webSources);
  } catch (InvalidOperationException exception) {
    Console.Error.WriteLine(exception.Message);
    return 1;
  }
  if (unformatted.Count > 0) {
    Console.Error.WriteLine("These staged web files are not formatted (or their imports are not sorted):");
    foreach (var path in unformatted) {
      Console.Error.WriteLine($"  web/{path}");
    }
    Console.Error.WriteLine("Run `mise web:format`, stage them again, and commit again.");
    return 1;
  }
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
