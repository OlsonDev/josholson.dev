#!/usr/bin/env dotnet
#:property PublishAot=false
// Keeps .editorconfig in step with the rules the analyzer packages ship (ADR 0030). A rule has been decided once
// `dotnet_diagnostic.<ID>.severity` appears for it in .editorconfig. Run through mise:
//   mise run analyzers:check   fail if an installed analyzer rule has no severity line, so an upgrade forces a decision
//   mise run analyzers:add     append the missing rules, set to what they do today, under the marker line
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string Project = "api/JoshOlson.Api";
const string EditorConfigPath = ".editorconfig";
const string Marker = "# analyzers:unreviewed";

var mode = args.FirstOrDefault();
if (mode is not ("check" or "add")) {
  Console.Error.WriteLine("Usage: dotnet run scripts/analyzers.cs -- check|add");
  return 2;
}

// The packages whose rules we decide, as they appear in the NuGet cache path.
string[] packages = [
  "meziantou.analyzer",
  "microsoft.codeanalysis.bannedapianalyzers",
  "roslynator.analyzers",
  "roslynator.formatting.analyzers",
];

var msbuild = Run("dotnet", "msbuild", Project, "-restore", "-t:ResolveLockFileAnalyzers",
  "-getProperty:RoslynTargetsPath", "-getItem:Analyzer");
using var json = JsonDocument.Parse(msbuild[msbuild.IndexOf('{')..]);
var roslynRoot = Path.GetFullPath(json.RootElement.GetProperty("Properties").GetProperty("RoslynTargetsPath").GetString()!);
var roslynDirectory = Path.Combine(roslynRoot, "bincore");
// Roslynator's analyzers also reference Microsoft.CodeAnalysis.Workspaces, which only dotnet-format carries.
var workspacesDirectory = Path.Combine(Path.GetDirectoryName(roslynRoot)!, "DotnetTools", "dotnet-format");
var dllPaths = json.RootElement.GetProperty("Items").GetProperty("Analyzer").EnumerateArray()
  .Select(item => item.GetProperty("Identity").GetString()!)
  .Where(path => packages.Any(package => path.Contains($"/{package}/", StringComparison.Ordinal)))
  .ToList();

// Analyzers reference Microsoft.CodeAnalysis (the SDK's copy is what the compiler loads) and their own helper DLLs.
var searchDirectories = dllPaths.Select(path => Path.GetDirectoryName(path)!).Distinct().Append(roslynDirectory).Append(workspacesDirectory).ToList();
AppDomain.CurrentDomain.AssemblyResolve += (_, resolve) => {
  var name = new AssemblyName(resolve.Name).Name + ".dll";
  var path = searchDirectories.Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
  return path is null ? null : Assembly.LoadFrom(path);
};

var skipped = new List<string>();
var rules = new SortedDictionary<string, Rule>(StringComparer.Ordinal);
foreach (var dll in dllPaths.Where(path => Path.GetFileName(path) is var file
    && !file.Contains("CodeFix", StringComparison.Ordinal) && !file.StartsWith("Roslynator_", StringComparison.Ordinal))) {
  var package = packages.First(package => dll.Contains($"/{package}/", StringComparison.Ordinal));
  var assembly = Assembly.LoadFrom(dll);
  Type?[] types;
  try {
    types = assembly.GetTypes();
  } catch (ReflectionTypeLoadException exception) {
    types = exception.Types;
  }
  foreach (var type in types.OfType<Type>().Where(type => IsAnalyzer(type, skipped))) {
    var analyzer = Activator.CreateInstance(type)!;
    var descriptors = (System.Collections.IEnumerable)type.GetProperty("SupportedDiagnostics")!.GetValue(analyzer)!;
    foreach (var descriptor in descriptors) {
      var id = (string)Get(descriptor, "Id")!;
      // Fade-out companions (RCS1002FadeOut) only grey out code and cannot be configured.
      if (((System.Collections.IEnumerable)Get(descriptor, "CustomTags")!).Cast<string>().Contains("NotConfigurable")) {
        continue;
      }
      rules[id] = new Rule(
        id,
        package,
        Get(descriptor, "Title")?.ToString() ?? "",
        Get(descriptor, "DefaultSeverity")!.ToString()!,
        (bool)Get(descriptor, "IsEnabledByDefault")!);
    }
  }
}

if (skipped.Count > 0) {
  Console.Error.WriteLine($"Could not load {skipped.Count} types, so their rules may be missing: {string.Join(", ", skipped)}");
  return 2;
}
if (rules.Count is 0) {
  Console.Error.WriteLine("No analyzer rules found. Is the project restored?");
  return 2;
}

var lines = File.ReadAllLines(EditorConfigPath).ToList();
var severityLine = new Regex(@"^\s*dotnet_diagnostic\.(?<id>[A-Za-z]+[0-9]+)\.severity\s*=", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
var configured = lines.Select(line => severityLine.Match(line)).Where(match => match.Success)
  .Select(match => match.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

var missing = rules.Values.Where(rule => !configured.Contains(rule.Id)).ToList();
var prefixes = rules.Keys.Select(id => Regex.Match(id, "^[A-Za-z]+").Value).ToHashSet(StringComparer.Ordinal);
var stale = configured.Where(id => prefixes.Contains(Regex.Match(id, "^[A-Za-z]+").Value) && !rules.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();

Console.WriteLine($"{rules.Count} rules in {packages.Length} packages; {missing.Count} without a severity in {EditorConfigPath}.");
if (stale.Count > 0) {
  Console.WriteLine($"Configured but no longer shipped: {string.Join(", ", stale)}");
}

// The scripts reference these analyzers from the root Directory.Build.props, which cannot use central package
// management, so its versions must equal the ones api/Directory.Packages.props pins.
var central = XDocument.Load("api/Directory.Packages.props").Descendants("PackageVersion")
  .ToDictionary(item => (string)item.Attribute("Include")!, item => (string)item.Attribute("Version")!, StringComparer.OrdinalIgnoreCase);
var drift = XDocument.Load("Directory.Build.props").Descendants("PackageReference")
  .Select(item => (Name: (string)item.Attribute("Include")!, Version: (string?)item.Attribute("Version")))
  .Where(package => central.TryGetValue(package.Name, out var version) && !string.Equals(version, package.Version, StringComparison.Ordinal))
  .ToList();
foreach (var package in drift) {
  Console.WriteLine($"Version drift: Directory.Build.props has {package.Name} {package.Version}, api/Directory.Packages.props has {central[package.Name]}.");
}

if (mode is "check") {
  foreach (var rule in missing) {
    Console.WriteLine($"  {rule.Id}  {rule.Package}  ({rule.Describe()})  {rule.Title}");
  }
  if (missing.Count > 0) {
    Console.WriteLine("Decide each one, or run `mise run analyzers:add` to append them at their current behavior.");
  }
  return missing.Count > 0 || drift.Count > 0 ? 1 : 0;
}

if (missing.Count is 0) {
  return 0;
}
var at = lines.FindIndex(line => string.Equals(line.Trim(), Marker, StringComparison.Ordinal));
if (at < 0) {
  Console.Error.WriteLine($"{EditorConfigPath} has no `{Marker}` line to add the rules under.");
  return 2;
}
lines.InsertRange(at + 1, missing.Select(rule => $"dotnet_diagnostic.{rule.Id}.severity = {rule.CurrentSeverity()}"));
File.WriteAllText(EditorConfigPath, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
Console.WriteLine($"Added {missing.Count} rules under `{Marker}`. Edit each in place once decided.");
return 0;

static bool IsAnalyzer(Type type, List<string> skipped) {
  try {
    if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null) {
      return false;
    }
    for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType) {
      if (baseType.FullName is "Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer") {
        return true;
      }
    }
    return false;
  } catch (Exception exception) when (exception is FileNotFoundException or TypeLoadException) {
    skipped.Add(type.FullName ?? type.Name);
    return false;
  }
}

static object? Get(object target, string property)
  => target.GetType().GetProperty(property)!.GetValue(target);

static string Run(string file, params string[] arguments) {
  var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
  foreach (var argument in arguments) {
    info.ArgumentList.Add(argument);
  }
  using var process = Process.Start(info)!;
  var output = process.StandardOutput.ReadToEnd();
  var error = process.StandardError.ReadToEnd();
  process.WaitForExit();
  if (process.ExitCode is not 0) {
    throw new InvalidOperationException($"{file} {string.Join(' ', arguments)} failed:\n{output}{error}");
  }
  return output;
}

internal record Rule(string Id, string Package, string Title, string DefaultSeverity, bool EnabledByDefault) {
  // Roslynator's rules are switched to warning as a category in .editorconfig, so an enabled one is a warning today.
  public string CurrentSeverity()
    => (EnabledByDefault, Package, DefaultSeverity) switch {
      (false, _, _) => "none",
      (_, "roslynator.analyzers", _) => "warning",
      (_, _, "Error") => "error",
      (_, _, "Warning") => "warning",
      (_, _, "Info") => "suggestion",
      _ => "silent",
    };

  public string Describe()
    => EnabledByDefault ? $"default {DefaultSeverity.ToLowerInvariant()}" : "off by default";
}
