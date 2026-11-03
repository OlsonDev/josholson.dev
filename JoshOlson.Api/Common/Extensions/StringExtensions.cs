using System.Text.Json;

namespace JoshOlson.Api.Common.Extensions;

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
