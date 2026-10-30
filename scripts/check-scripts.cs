#!/usr/bin/env dotnet
// Builds every loose script (scripts/, .githooks/) so the style rules and analyzers in the root Directory.Build.props
// run on them (ADR 0030). `dotnet format` cannot open file-based apps, so this build is the check. Run through mise:
//   mise run scripts:check
using System.Diagnostics;

var entryPoints = new[] { "scripts", ".githooks" }
  .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs"))
  .Where(path => File.ReadLines(path).FirstOrDefault()?.StartsWith("#!", StringComparison.Ordinal) is true)
  .Order(StringComparer.Ordinal)
  .ToList();

var failed = 0;
foreach (var path in entryPoints) {
  var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
  foreach (var argument in new[] { "build", path, "--nologo", "-v:q" }) {
    info.ArgumentList.Add(argument);
  }
  using var process = Process.Start(info)!;
  var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
  var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
  await process.WaitForExitAsync(CancellationToken.None);
  if (process.ExitCode is 0) {
    Console.WriteLine($"✓ {path}");
    continue;
  }
  failed++;
  Console.WriteLine($"✗ {path}");
  var output = await standardOutput + await standardError;
  foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)) {
    Console.WriteLine($"  {line.Trim()}");
  }
}

Console.WriteLine($"{entryPoints.Count - failed} of {entryPoints.Count} scripts pass.");
return failed > 0 ? 1 : 0;
