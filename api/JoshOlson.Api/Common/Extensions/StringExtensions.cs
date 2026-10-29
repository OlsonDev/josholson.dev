using System.Text.Json;

namespace JoshOlson.Api.Common.Extensions;

public static class StringExtensions {
  // Camel-cases each dotted segment the way System.Text.Json does, and leaves index suffixes alone:
  // `Children[0].Label` becomes `children[0].label`, `URL` becomes `url`.
  public static string ToCamelCasePath(this string path)
    => string.Join('.', path.Split('.').Select(CamelCaseSegment));

  private static string CamelCaseSegment(string segment) {
    var bracket = segment.IndexOf('[');
    return bracket < 0
      ? JsonNamingPolicy.CamelCase.ConvertName(segment)
      : JsonNamingPolicy.CamelCase.ConvertName(segment[..bracket]) + segment[bracket..];
  }
}
