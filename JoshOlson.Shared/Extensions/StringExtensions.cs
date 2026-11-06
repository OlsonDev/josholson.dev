using System.Text;
using System.Text.Json;

namespace JoshOlson.Shared.Extensions;

public static class StringExtensions {
  extension(string? value) {
    // Trimmed, or null when nothing is left: the form nullable text columns take (a value or NULL, never '').
    public string? TrimToNull() {
      var trimmed = value?.Trim();
      return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
  }

  extension(string value) {
    // At most `maxLength` characters. Shorter text is returned as it is.
    public string Truncate(int maxLength)
      => value.Length <= maxLength ? value : value[..maxLength];

    // The database's text.kebab_case: lowercase letters and digits in words joined by single hyphens.
    public bool IsKebabCase()
      => value.Length > 0
      && value[0] is not '-'
      && value[^1] is not '-'
      && !value.Contains("--", StringComparison.Ordinal)
      && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-');

    // PascalCase as lowercase words joined by hyphens: `SetNull` becomes `set-null`. Digits stay with the word before.
    public string ToKebabCase() {
      var kebab = new StringBuilder(value.Length + 4);
      foreach (var character in value) {
        if (char.IsAsciiLetterUpper(character)) {
          if (kebab.Length > 0) {
            kebab.Append('-');
          }
          kebab.Append(char.ToLowerInvariant(character));
        } else {
          kebab.Append(character);
        }
      }
      return kebab.ToString();
    }

    // Camel-cases each dotted segment the way System.Text.Json does, and leaves index suffixes alone:
    // `Children[0].Label` becomes `children[0].label`, `URL` becomes `url`.
    public string ToCamelCasePath()
      => string.Join('.', value.Split('.').Select(CamelCaseSegment));
  }

  private static string CamelCaseSegment(string segment) {
    var bracket = segment.IndexOf('[');
    return bracket < 0
      ? JsonNamingPolicy.CamelCase.ConvertName(segment)
      : JsonNamingPolicy.CamelCase.ConvertName(segment[..bracket]) + segment[bracket..];
  }
}
