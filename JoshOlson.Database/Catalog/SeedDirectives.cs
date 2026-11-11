using JoshOlson.Database.Schema;
using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Catalog;

// The `@seed` directives a comment can carry. On a column (`COMMENT ON COLUMN`), `@seed absolute` opts a `timestamptz`
// column out of relative times (ADR 0035, typed values and relative times) and `@seed relative` says the default out
// loud. On a table (`COMMENT ON TABLE`), `@seed ignore` says its rows are runtime data and not seed data.
internal static class SeedDirectives {
  private const string _Marker = "@seed";
  private const string _Ignore = "ignore";

  // Whether a table's comment says `@seed ignore`, and the problem, if the directive is bad. Like a column's, a directive
  // with a missing or unknown word is a problem rather than being ignored: a typo would otherwise leave a runtime table in
  // every export. `where` names the table in the problem.
  public static (bool Ignored, string? Problem) TableIgnoreOf(string? comment, string where) {
    if (comment is null) {
      return (false, null);
    }
    var words = comment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    var ignored = false;
    for (var index = 0; index < words.Length; index++) {
      if (!string.Equals(words[index], _Marker, StringComparison.Ordinal)) {
        continue;
      }
      var word = index + 1 < words.Length ? words[index + 1] : "";
      if (!string.Equals(word, _Ignore, StringComparison.Ordinal)) {
        return (false, $"{where}: the comment has '{_Marker} {word}', which is not '{_Marker} {_Ignore}'.");
      }
      ignored = true;
    }
    return (ignored, null);
  }

  // The policy the comment asks for, or null when it has no directive; and the problem, if the directive is bad. A
  // directive with a missing or unknown word, or two that disagree, is a problem rather than being ignored: a typo
  // would otherwise turn a column relative. `where` names the column in the problem.
  public static (TimePolicy? Policy, string? Problem) TimePolicyOf(string? comment, string where) {
    if (comment is null) {
      return (null, null);
    }
    var words = comment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    TimePolicy? policy = null;
    for (var index = 0; index < words.Length; index++) {
      if (!string.Equals(words[index], _Marker, StringComparison.Ordinal)) {
        continue;
      }
      var word = index + 1 < words.Length ? words[index + 1] : "";
      if (!Enum.TryParseKebabCase<TimePolicy>(word, out var found)) {
        return (null, $"{where}: the comment has '{_Marker} {word}', which is not '{_Marker} relative' or '{_Marker} absolute'.");
      }
      if (policy is { } earlier && earlier != found) {
        return (null, $"{where}: the comment says both '{_Marker} relative' and '{_Marker} absolute'.");
      }
      policy = found;
    }
    return (policy, null);
  }
}
