#!/usr/bin/env dotnet
// Run with: dotnet run setup.cs
var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
  FileName = "git",
  Arguments = "config core.hooksPath .githooks",
  UseShellExecute = false
})!;
process.WaitForExit();

if (process.ExitCode == 0) Console.WriteLine("Git hooks configured.");
else Console.Error.WriteLine("Failed to configure git hooks.");

return process.ExitCode;
