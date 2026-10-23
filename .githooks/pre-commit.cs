#!/usr/bin/env dotnet
#:include branch-name-validator.cs

using System.Diagnostics;

var gitDir = Git("rev-parse --git-dir");
// Branch names don't matter while a rebase replays commits
if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return 0;

var (success, error) = BranchNameValidator.ValidateBranchName(Git("branch --show-current"));
if (success) return 0;
Console.Error.WriteLine(error);
return 1;

static string Git(string arguments) {
  var startInfo = new ProcessStartInfo("git", arguments) { RedirectStandardOutput = true, UseShellExecute = false };
  using var process = Process.Start(startInfo)!;
  var output = process.StandardOutput.ReadToEnd().Trim();
  process.WaitForExit();
  return output;
}
