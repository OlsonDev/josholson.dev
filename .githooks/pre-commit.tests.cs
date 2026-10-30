#!/usr/bin/env dotnet
// Unit tests for pre-commit.cs
// Run with: dotnet run .githooks/pre-commit.tests.cs

#:include branch-name-validator.cs
#:include analyzer-check-trigger.cs

var tests = new List<TestCase> {
  // Valid cases
  BranchName.Valid("main", "main"),
  BranchName.Valid("feature", "feature/foo-bar"),
  BranchName.Valid("mob", "mob/foo"),
  BranchName.Valid("fix", "fix/foo"),
  BranchName.Valid("hotfix", "hotfix/foo"),
  BranchName.Valid("docs", "docs/foo"),
  BranchName.Valid("test", "test/foo"),
  BranchName.Valid("digits and uppercase after the first letter", "feature/Foo-2-Bar"),

  // Invalid: prefix issues
  BranchName.Invalid("unknown prefix", "chore/foo", "Start it with one of"),
  BranchName.Invalid("no prefix", "foo", "Start it with one of"),
  BranchName.Invalid("no trailing slash", "feature-foo", "Start it with one of"),
  BranchName.Invalid("prefix only", "feature/", "start with a letter"),
  BranchName.Invalid("detached HEAD", "", "Start it with one of"),
  BranchName.Invalid("master", "master", "Start it with one of"),

  // Invalid: name issues
  BranchName.Invalid("name starts with a digit", "feature/1-foo", "start with a letter"),
  BranchName.Invalid("name starts with a hyphen", "feature/-foo", "start with a letter"),
  BranchName.Invalid("underscore in name", "feature/foo_bar", "only letters, digits, and hyphens"),
  BranchName.Invalid("second slash", "feature/foo/bar", "only letters, digits, and hyphens"),
};

var countPassed = 0;
var countFailed = 0;

foreach (var test in tests) {
  var (success, error) = BranchNameValidator.ValidateBranchName(test.Input);

  var outcomeMatches = test.ShouldPass == success;
  var errorMatches = test.ExpectedError is null || error!.Contains(test.ExpectedError);
  var testPassed = outcomeMatches && errorMatches;

  if (testPassed) {
    Console.WriteLine($"✓ {test.DisplayName}");
    countPassed++;
  } else {
    Console.WriteLine($"✗ {test.DisplayName}");
    Console.WriteLine($"  Input: \"{test.Input}\"");
    Console.WriteLine($"  Expected: {(test.ShouldPass ? "pass" : $"fail with \"{test.ExpectedError}\"")}");
    Console.WriteLine($"  Actual: {(success ? "passed" : $"failed with \"{error}\"")}");
    countFailed++;
  }
}

string[][] triggers = [
  ["api/Directory.Packages.props"],
  ["README.md", ".editorconfig"],
  ["api/.editorconfig"],
  ["api/JoshOlson.Api/JoshOlson.Api.csproj"],
  ["api/Directory.Build.props"],
  ["scripts/analyzers.cs"],
];
string[][] nonTriggers = [
  [],
  ["README.md"],
  ["api/JoshOlson.Api/Tags/TagController.cs"],
  ["scripts/missing-tests.cs"],
  ["docs/editorconfig.md"],
];
foreach (var (paths, expected) in triggers.Select(paths => (paths, true)).Concat(nonTriggers.Select(paths => (paths, false)))) {
  var needed = AnalyzerCheckTrigger.IsNeeded(paths);
  if (needed == expected) {
    Console.WriteLine($"✓ Analyzer check {(expected ? "runs" : "is skipped")} for [{string.Join(", ", paths)}]");
    countPassed++;
  } else {
    Console.WriteLine($"✗ Analyzer check {(expected ? "should run" : "should be skipped")} for [{string.Join(", ", paths)}]");
    countFailed++;
  }
}

string[][] scriptTriggers = [
  ["scripts/schema-docs.cs"],
  [".githooks/pre-commit.cs"],
  ["api/JoshOlson.Api/Tags/TagController.cs", "scripts/analyzers.cs"],
  ["Directory.Build.props"],
  [".editorconfig"],
];
string[][] scriptNonTriggers = [
  [],
  ["README.md"],
  ["api/JoshOlson.Api/Tags/TagController.cs"],
  ["scripts/README.md"],
  ["api/Directory.Packages.props"],
  ["docs/adr/0030-analyzers.md"],
];
foreach (var (paths, expected) in scriptTriggers.Select(paths => (paths, true)).Concat(scriptNonTriggers.Select(paths => (paths, false)))) {
  var needed = AnalyzerCheckTrigger.IsScriptCheckNeeded(paths);
  if (needed == expected) {
    Console.WriteLine($"✓ Script check {(expected ? "runs" : "is skipped")} for [{string.Join(", ", paths)}]");
    countPassed++;
  } else {
    Console.WriteLine($"✗ Script check {(expected ? "should run" : "should be skipped")} for [{string.Join(", ", paths)}]");
    countFailed++;
  }
}

Console.WriteLine();
Console.WriteLine($"Results: {countPassed} passed, {countFailed} failed");

return countFailed > 0 ? 1 : 0;

// A case passes validation (no ExpectedError) or fails with a specific error (ExpectedError), never both
internal record TestCase(string Name, string Input, string? ExpectedError = null) {
  public bool ShouldPass
    => ExpectedError is null;

  public string DisplayName
    => $"{(ShouldPass ? "Valid" : "Invalid")}: {Name}";
}

internal static class BranchName {
  public static TestCase Valid(string name, string input)
    => new(name, input);

  public static TestCase Invalid(string name, string input, string expectedError)
    => new(name, input, expectedError);
}
