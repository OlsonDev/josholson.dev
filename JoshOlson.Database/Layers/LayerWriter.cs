using System.Globalization;
using System.Text;
using System.Text.Json;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Time;
using NodaTime.Text;

namespace JoshOlson.Database.Layers;

// Writes a layer as text that `LayerParser` reads back to the same layer, and that is the only text it writes for it
// (ADR 0034, lock file and branch-safe migration: `seeds:check` needs a layer to export to its own file, byte for byte).
// So there is no choice anywhere in it:
// - the keys are `description`, `extends` (left out for a root), and `tables`, in that order;
// - the tables are sorted by name, a table with nothing to change is left out, `upsert` comes before `delete`, and the
//   rows are sorted by primary key (`RowKeyComparer`);
// - a row is a one-line flow mapping with its columns in the table's order, and `~` for NULL;
// - a value is written the way `SeedValueReader` reads it: an offset like `-3d 4h` for a relative time, a UTC instant for
//   an absolute one, `\x` and hex for bytes, native YAML for `jsonb` (every string in it quoted), and a YAML list for an
//   array;
// - text is plain when YAML would read it back as the same text, and double-quoted otherwise.
public static class LayerWriter {
  private const string _Indicators = "?:,[]{}#&*!|>'\"%@`\\";

  public static string Write(SeedLayer layer, SeedSchema schema) {
    var text = new StringBuilder();
    _ = text.Append("description: ").Append(Scalar(layer.Description, inFlow: false)).Append('\n');
    if (layer.Extends is not null) {
      _ = text.Append("extends: ").Append(layer.Extends).Append('\n');
    }
    var changed = layer.Tables
      .Where(table => table.Upserts.Count + table.Deletes.Count > 0)
      .OrderBy(table => table.Table, StringComparer.Ordinal)
      .ToList();
    if (changed.Count > 0) {
      _ = text.Append("tables:\n");
    }
    foreach (var table in changed) {
      var known = schema.Find(table.Table)
        ?? throw new SeedFormatException($"{layer.Name}: {table.Table} is not a table in the schema lock.");
      _ = text.Append("  ").Append(table.Table).Append(":\n");
      Section(text, "upsert", table.Upserts, known);
      Section(text, "delete", table.Deletes, known);
    }
    return text.ToString();
  }

  private static void Section(StringBuilder text, string name, List<Dictionary<string, SeedValue?>> rows, SeedTable table) {
    if (rows.Count is 0) {
      return;
    }
    _ = text.Append("    ").Append(name).Append(":\n");
    var ordered = rows.OrderBy(row => KeyOf(row, table), RowKeyComparer.Instance);
    foreach (var row in ordered) {
      var unknown = row.Keys.FirstOrDefault(column => !table.Columns.ContainsKey(column));
      if (unknown is not null) {
        throw new SeedFormatException($"{table.Table.Name} has no column {unknown}.");
      }
      var cells = table.Table.Columns
        .Where(column => row.ContainsKey(column.Name))
        .Select(column => $"{column.Name}: {Value(row[column.Name])}");
      _ = text.Append("      - { ").AppendJoin(", ", cells).Append(" }\n");
    }
  }

  private static SeedValue[] KeyOf(Dictionary<string, SeedValue?> row, SeedTable table)
    => [.. table.Table.PrimaryKey.Select(column => row[column]!)];

  private static string Value(SeedValue? value)
    => value switch {
      null => "~",
      SeedValue.Text text => Scalar(text.Value, inFlow: true),
      SeedValue.Boolean boolean => boolean.Value ? "true" : "false",
      SeedValue.Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
      SeedValue.Decimal number => number.Value.ToString(CultureInfo.InvariantCulture),
      SeedValue.RelativeTime offset => DurationText.Format(offset.Microseconds),
      SeedValue.AbsoluteTime instant => InstantPattern.ExtendedIso.Format(instant.Value),
      SeedValue.Bytes bytes => "\\x" + bytes.Hex,
      SeedValue.Json json => Json(JsonDocument.Parse(json.Value).RootElement),
      SeedValue.Inet inet => Scalar(inet.Value, inFlow: true),
      SeedValue.Array array => "[" + string.Join(", ", array.Items.Select(Value)) + "]",
      _ => throw new InvalidOperationException($"No way to write a {value.GetType().Name}."),
    };

  // `jsonb` as YAML: a plain `true`, `false`, `null`, and number are those JSON values, and anything quoted is a string,
  // so every string is quoted (a key, which is always a string, is plain only when it need not be quoted).
  private static string Json(JsonElement element)
    => element.ValueKind switch {
      JsonValueKind.Object => element.EnumerateObject().Any()
        ? "{ " + string.Join(", ", element.EnumerateObject().Select(property => $"{Scalar(property.Name, inFlow: true)}: {Json(property.Value)}")) + " }"
        : "{}",
      JsonValueKind.Array => "[" + string.Join(", ", element.EnumerateArray().Select(Json)) + "]",
      JsonValueKind.String => Quote(element.GetString()!),
      JsonValueKind.Number => element.GetRawText(),
      JsonValueKind.True => "true",
      JsonValueKind.False => "false",
      _ => "null",
    };

  // A scalar that YAML reads back as exactly this text: plain when that is safe, double-quoted when it is not. Plain is
  // unsafe for the words that mean NULL (`~`, `null`, and nothing at all), for anything that starts with a YAML indicator,
  // has a space at either end, a `: ` or ` #` inside it, a control character, and, in a one-line mapping or list
  // (`inFlow`), a comma, bracket, or brace.
  private static string Scalar(string text, bool inFlow)
    => IsPlain(text, inFlow) ? text : Quote(text);

  private static bool IsPlain(string text, bool inFlow) {
    if (text is "" or "~" or "null" || text[0] == ' ' || text[^1] == ' ' || _Indicators.Contains(text[0], StringComparison.Ordinal)) {
      return false;
    }
    if (text[0] == '-' && (text.Length is 1 || text[1] == ' ')) {
      return false;
    }
    for (var i = 0; i < text.Length; i++) {
      var c = text[i];
      var isFlowIndicator = inFlow && c is ',' or '[' or ']' or '{' or '}';
      var isCommentStart = c == '#' && i > 0 && text[i - 1] == ' ';
      var isMappingColon = c == ':' && (i == text.Length - 1 || text[i + 1] == ' ');
      if (IsControl(c) || isFlowIndicator || isCommentStart || isMappingColon) {
        return false;
      }
    }
    return true;
  }

  // A control character, or the Unicode line and paragraph separators (U+2028 and U+2029), which YAML also treats as a
  // line break.
  private static bool IsControl(char c)
    => char.IsControl(c) || c is (char)0x2028 or (char)0x2029;

  private static string Quote(string text) {
    var quoted = new StringBuilder(text.Length + 2).Append('"');
    foreach (var c in text) {
      _ = c switch {
        '"' => quoted.Append("\\\""),
        '\\' => quoted.Append("\\\\"),
        '\n' => quoted.Append("\\n"),
        '\r' => quoted.Append("\\r"),
        '\t' => quoted.Append("\\t"),
        _ when IsControl(c) => quoted.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"),
        _ => quoted.Append(c),
      };
    }
    return quoted.Append('"').ToString();
  }
}
