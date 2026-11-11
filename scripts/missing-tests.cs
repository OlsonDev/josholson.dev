#!/usr/bin/env dotnet
#:project ../JoshOlson.Shared
#:property PublishAot=false
// Lists source files in the projects that have no sibling *.Tests.cs, so gaps show up without opening every folder.
// Run through mise: mise run test:missing. Pass --strict to exit 1 when anything is missing.
// Data-only and wiring files (`*.Payloads.cs`, `*Registration.cs`, `Program.cs`) need no test.
using System.Text.RegularExpressions;
using JoshOlson.Shared.IO;

var strict = args.Contains("--strict");
var untested = new Regex(@"(Payloads|Registration)\.cs$|^Program\.cs$");
var missing = SourceFiles.Enumerate(["JoshOlson.Api", "JoshOlson.Database", "JoshOlson.Seed", "JoshOlson.Shared"], "*.cs")
  .Where(path => !path.EndsWith(".Tests.cs", StringComparison.Ordinal) && !untested.IsMatch(Path.GetFileName(path)) && !File.Exists(Path.ChangeExtension(path, ".Tests.cs")))
  .ToList();

foreach (var path in missing) Console.WriteLine(path);
Console.WriteLine($"{missing.Count} file(s) without tests.");
return strict && missing.Count > 0 ? 1 : 0;
