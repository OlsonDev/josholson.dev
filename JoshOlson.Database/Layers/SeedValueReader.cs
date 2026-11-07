using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Extensions;
using JoshOlson.Shared.Time;
using NodaTime.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Layers;

// Reads one YAML value as its column's type (ADR 0035, typed seed values). A scalar is the text that was written, never
// what YAML would infer from it, so `1000` into a `text` column is the text "1000". A plain `~`, `null`, or empty value is
// NULL and a quoted one is text. `Read` returns the problem as a sentence that completes "<column> ...", or null when the
// value is fine, so a caller can collect every problem in a layer.
//
// How each type is written:
// - `boolean`: `true` or `false`.
// - `smallint`, `integer`, `bigint`: digits with an optional sign, inside the type's range.
// - `decimal`: digits with an optional sign and point; a `decimal(p, s)` column refuses more digits than it holds, since
//   Postgres would round them.
// - `timestamptz`: an offset like `-3d 4h 12m 0.0123s` (`DurationText`) in a relative column, and a UTC instant like
//   `2026-01-02T03:04:05.123456Z` in one marked `@seed absolute`. Neither may be finer than a microsecond.
// - `bytea`: `\x` and hex digits, as Postgres prints it.
// - `jsonb`: native YAML, written out as JSON. Inside it a plain `true`, `false`, `null`, and number are those JSON values.
// - `inet`: an address, with `/bits` for a network.
// - an array (a column's, or a domain's): a YAML list, each item written as the base type, and a plain null item is NULL.
internal static class SeedValueReader {
  public static string? Read(SeedColumn column, YamlNode node, out SeedValue? value) {
    value = null;
    if (IsNull(node)) {
      return column.Column.Nullable ? null : "is NULL, but the column is NOT NULL.";
    }
    if (!column.Type.IsArray) {
      return ReadScalar(column, node, out value);
    }
    if (node is not YamlSequenceNode sequence) {
      return "is an array, so its value must be a list.";
    }
    var items = new List<SeedValue?>(sequence.Children.Count);
    for (var i = 0; i < sequence.Children.Count; i++) {
      var item = sequence.Children[i];
      if (IsNull(item)) {
        items.Add(null);
        continue;
      }
      if (ReadScalar(column, item, out var read) is { } problem) {
        return $"item {(i + 1).ToInvariantString()} {problem}";
      }
      items.Add(read);
    }
    value = new SeedValue.Array(items);
    return null;
  }

  private static bool IsNull(YamlNode node)
    => node is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "~" or "null" };

  private static string? ReadScalar(SeedColumn column, YamlNode node, out SeedValue? value) {
    value = null;
    var type = column.Type;
    if (type.Seed is SeedType.Jsonb) {
      value = new SeedValue.Json(JsonText(node));
      return null;
    }
    if (node is not YamlScalarNode { Value: { } text }) {
      return "must be a single value.";
    }
    return type.Seed switch {
      SeedType.Text => Done(new SeedValue.Text(text), out value),
      SeedType.Boolean => ReadBoolean(text, out value),
      SeedType.SmallInt => ReadInteger(text, short.MinValue, short.MaxValue, "smallint", out value),
      SeedType.Integer => ReadInteger(text, int.MinValue, int.MaxValue, "integer", out value),
      SeedType.BigInt => ReadInteger(text, long.MinValue, long.MaxValue, "bigint", out value),
      SeedType.Decimal => ReadDecimal(text, type.Modifiers, out value),
      SeedType.TimestampTz => column.Column.Policy is TimePolicy.Absolute
        ? ReadInstant(text, out value)
        : ReadOffset(text, out value),
      SeedType.Bytea => ReadBytes(text, out value),
      SeedType.Inet => ReadInet(text, out value),
      _ => throw new InvalidOperationException($"No reader for the seed type {type.Seed}."),
    };
  }

  private static string? Done(SeedValue read, out SeedValue? value) {
    value = read;
    return null;
  }

  private static string? ReadBoolean(string text, out SeedValue? value) {
    value = null;
    return text switch {
      "true" => Done(new SeedValue.Boolean(true), out value),
      "false" => Done(new SeedValue.Boolean(false), out value),
      _ => $"must be true or false, not '{text}'.",
    };
  }

  private static string? ReadInteger(string text, long minimum, long maximum, string type, out SeedValue? value) {
    value = null;
    if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)) {
      return $"must be a whole number, not '{text}'.";
    }
    if (number < minimum || number > maximum) {
      return $"is {text}, outside the range of {type}.";
    }
    value = new SeedValue.Integer(number);
    return null;
  }

  private static string? ReadDecimal(string text, string? modifiers, out SeedValue? value) {
    value = null;
    const NumberStyles Style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    if (!decimal.TryParse(text, Style, CultureInfo.InvariantCulture, out var number)) {
      return $"must be a number, not '{text}'.";
    }
    if (modifiers is not null && DigitsProblem(text, modifiers) is { } problem) {
      return problem;
    }
    value = new SeedValue.Decimal(number);
    return null;
  }

  // `decimal(p, s)` holds p digits in all, s of them after the point. `decimal(p)` is `decimal(p, 0)`.
  private static string? DigitsProblem(string text, string modifiers) {
    var numbers = modifiers.Split(", ");
    var precision = int.Parse(numbers[0], CultureInfo.InvariantCulture);
    var scale = numbers.Length > 1 ? int.Parse(numbers[1], CultureInfo.InvariantCulture) : 0;
    var unsigned = text.TrimStart('+', '-');
    var point = unsigned.IndexOf('.', StringComparison.Ordinal);
    var whole = (point < 0 ? unsigned : unsigned[..point]).TrimStart('0');
    var fraction = point < 0 ? "" : unsigned[(point + 1)..];
    if (whole.Length > precision - scale) {
      return $"has {whole.Length.ToInvariantString()} digits before the point, and decimal({modifiers}) holds {(precision - scale).ToInvariantString()}.";
    }
    return fraction.Length > scale
      ? $"has {fraction.Length.ToInvariantString()} digits after the point, and decimal({modifiers}) holds {scale.ToInvariantString()}."
      : null;
  }

  private static string? ReadOffset(string text, out SeedValue? value) {
    value = null;
    if (!DurationText.TryParse(text, out var microseconds)) {
      return $"must be an offset like -3d 4h 12m 0.0123s, not '{text}'.";
    }
    value = new SeedValue.RelativeTime(microseconds);
    return null;
  }

  private static string? ReadInstant(string text, out SeedValue? value) {
    value = null;
    var parsed = InstantPattern.ExtendedIso.Parse(text);
    if (!parsed.Success) {
      return $"must be a UTC instant like 2026-01-02T03:04:05.123456Z, not '{text}'.";
    }
    if (parsed.Value.InUtc().NanosecondOfSecond % 1000 is not 0) {
      return $"'{text}' is finer than a microsecond, which is as precise as Postgres is.";
    }
    value = new SeedValue.AbsoluteTime(parsed.Value);
    return null;
  }

  private static string? ReadBytes(string text, out SeedValue? value) {
    value = null;
    var hex = text.StartsWith("\\x", StringComparison.Ordinal) ? text[2..] : null;
    if (hex is null || hex.Length % 2 is not 0 || !hex.All(char.IsAsciiHexDigit)) {
      return $"must be \\x and an even number of hex digits, not '{text}'.";
    }
    value = new SeedValue.Bytes(hex.ToLowerInvariant());
    return null;
  }

  private static string? ReadInet(string text, out SeedValue? value) {
    value = null;
    var parts = text.Split('/');
    if (parts.Length > 2
        || !(parts[0].Contains('.', StringComparison.Ordinal) || parts[0].Contains(':', StringComparison.Ordinal))
        || !IPAddress.TryParse(parts[0], out var address)) {
      return $"must be an IP address, with /bits for a network, not '{text}'.";
    }
    var maximum = address.AddressFamily is AddressFamily.InterNetwork ? 32 : 128;
    if (parts.Length is 1) {
      value = new SeedValue.Inet(address.ToString());
      return null;
    }
    if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var bits) || bits > maximum) {
      return $"has a network size of '{parts[1]}', and an address of this kind takes 0 to {maximum.ToInvariantString()}.";
    }
    value = new SeedValue.Inet(string.Create(CultureInfo.InvariantCulture, $"{address}/{bits}"));
    return null;
  }

  // The JSON a `jsonb` value is: a mapping is an object, a list is an array, and a scalar is a string when it was quoted.
  private static string JsonText(YamlNode node) {
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
      WriteJson(writer, node);
    }
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  private static void WriteJson(Utf8JsonWriter writer, YamlNode node) {
    switch (node) {
      case YamlMappingNode mapping:
        writer.WriteStartObject();
        foreach (var (key, child) in mapping.Children) {
          writer.WritePropertyName(key is YamlScalarNode { Value: { } name } ? name : "");
          WriteJson(writer, child);
        }
        writer.WriteEndObject();
        break;
      case YamlSequenceNode sequence:
        writer.WriteStartArray();
        foreach (var child in sequence.Children) {
          WriteJson(writer, child);
        }
        writer.WriteEndArray();
        break;
      case YamlScalarNode scalar:
        WriteJsonScalar(writer, scalar);
        break;
      default:
        throw new InvalidOperationException($"Unexpected YAML node {node.GetType().Name}.");
    }
  }

  private static void WriteJsonScalar(Utf8JsonWriter writer, YamlScalarNode scalar) {
    var text = scalar.Value ?? "";
    if (scalar.Style is not ScalarStyle.Plain) {
      writer.WriteStringValue(text);
    } else if (IsNull(scalar)) {
      writer.WriteNullValue();
    } else if (text is "true" or "false") {
      writer.WriteBooleanValue(text is "true");
    } else if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) {
      writer.WriteNumberValue(whole);
    } else if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) {
      writer.WriteNumberValue(number);
    } else {
      writer.WriteStringValue(text);
    }
  }
}
