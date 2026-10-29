#!/usr/bin/env dotnet
// Lists api source files that have no sibling *.Tests.cs, so gaps show up without opening every folder.
// Run through mise: mise run test:missing. Pass --strict to exit 1 when anything is missing.
// Data-only and wiring files (`*.Payloads.cs`, `*Registration.cs`, `Program.cs`) need no test.
using System.Text.RegularExpressions;

var strict = args.Contains("--strict");
var untested = new Regex(@"(Payloads|Registration)\.cs$|^Program\.cs$");
var missing = Directory.EnumerateFiles("api", "*.cs", SearchOption.AllDirectories)
  .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
  .Where(path => !path.EndsWith(".Tests.cs") && !untested.IsMatch(Path.GetFileName(path)))
  .Where(path => !File.Exists(Path.ChangeExtension(path, ".Tests.cs")))
  .Order()
  .ToList();

foreach (var path in missing) Console.WriteLine(path);
Console.WriteLine($"{missing.Count} file(s) without tests.");
return strict && missing.Count > 0 ? 1 : 0;
