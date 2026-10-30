#!/usr/bin/env dotnet
// Unit tests for commit-msg.cs
// Run with: dotnet run .githooks/commit-msg.tests.cs

#:include commit-msg-validator.cs

var tests = new List<TestCase> {
  // Valid cases
  CommitMsg.Valid("single emoji", "✨ Add new feature"),
  CommitMsg.Valid("two emojis", "✨🚀 Add new feature"),
  CommitMsg.Valid("rocket emoji", "🚀 Deploy to production"),
  CommitMsg.Valid("bug fix", "🐛 Fix login issue"),

  // Invalid: emoji issues
  CommitMsg.Invalid("no emoji", "Add new feature", "start with 1-2 emojis"),
  CommitMsg.Invalid("three emojis", "✨🚀🎨 Add feature", "use at most 2 emojis"),

  // Invalid: spacing issues
  CommitMsg.Invalid("no space after emoji", "✨Add new feature", "add a space after your emoji"),
  CommitMsg.Invalid("two spaces after emoji", "✨  Add new feature", "use only one space after your emoji"),
  CommitMsg.Invalid("leading whitespace", "  ✨ Add feature", "remove all leading whitespace"),
  CommitMsg.Invalid("trailing whitespace", "✨ Add new feature   ", "remove all trailing whitespace"),

  // Invalid: message issues
  CommitMsg.Invalid("message too short", "✨ Fix", "add a meaningful message"),
  CommitMsg.Invalid("no message", "✨ ", "add a meaningful message"),
  CommitMsg.Invalid("emoji only", "✨", "add a space after your emoji"),

  // Invalid: emoji in message
  CommitMsg.Invalid("emoji in message", "✨ Add new feature 🎉", "remove emojis from your message"),
  CommitMsg.Invalid("emoji at end", "✨ Fix the bug 🐛", "remove emojis from your message"),
};

var countPassed = 0;
var countFailed = 0;

foreach (var test in tests) {
  var (success, error) = CommitMessageValidator.ValidateCommitMessage(test.Input);

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

internal static class CommitMsg {
  public static TestCase Valid(string name, string input)
    => new(name, input);

  public static TestCase Invalid(string name, string input, string expectedError)
    => new(name, input, expectedError);
}
