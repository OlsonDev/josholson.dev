using System.Text.RegularExpressions;

static partial class BranchNameValidator {
  // Requirements:
  // - main (not protected yet), or
  // - a prefix from AllowedPrefixes with a trailing slash, then a name
  // - the name starts with a letter and has only letters, digits, and hyphens
  static readonly string[] AllowedPrefixes = ["mob", "feature", "fix", "hotfix", "docs", "test"];

  [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
  private static partial Regex NamePattern();

  public static (bool Success, string? Error) ValidateBranchName(string branchName) {
    if (branchName == "main") return (true, null);
    var slash = branchName.IndexOf('/');
    if (slash < 0 || !AllowedPrefixes.Contains(branchName[..slash])) {
      var prefixes = string.Join(", ", AllowedPrefixes.Select(p => $"\"{p}/\""));
      return (false, $"Invalid branch name \"{branchName}\". Start it with one of {prefixes}.");
    }
    return NamePattern().IsMatch(branchName[(slash + 1)..])
      ? (true, null)
      : (false, $"Invalid branch name \"{branchName}\". After the prefix, start with a letter and use only letters, digits, and hyphens.");
  }
}
