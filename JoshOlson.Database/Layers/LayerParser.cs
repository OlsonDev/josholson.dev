using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Layers;

// Reads one layer file:
//
//   description: What this layer adds, in a sentence
//   extends: parent-layer        # omit for a root layer
//   tables:
//     foo.bar:                   # schema.table
//       upsert:
//         - { bar_id: 1000, name: baz, qux: ~ }
//       delete:
//         - { bar_id: 1001 }     # primary key columns only
//
// Every value is text (Postgres casts it on insert), and `~`, `null`, or an empty value is NULL.
public static class LayerParser {
  private static readonly string[] _LayerKeys = [
    "description",
    "extends",
    "tables",
  ];
  private static readonly string[] _TableKeys = [
    "upsert",
    "delete",
  ];

  public static SeedLayer Parse(string name, string yaml) {
    if (!LayerName.IsValid(name)) {
      throw new SeedFormatException($"'{name}' is not a layer name: use lower-kebab-case, 40 characters at most.");
    }
    var root = LoadRoot(name, yaml);
    CheckKeys(name, "the layer", root, _LayerKeys);

    var description = Scalar(name, "description", Required(name, root, "description"));
    if (string.IsNullOrWhiteSpace(description)) {
      throw new SeedFormatException($"{name}: description is empty.");
    }
    string? extends = null;
    if (Find(root, "extends") is { } extendsNode) {
      extends = Scalar(name, "extends", extendsNode);
      if (extends is null || !LayerName.IsValid(extends)) {
        throw new SeedFormatException($"{name}: extends '{extends}' is not a layer name.");
      }
    }

    List<TableChange> tables = [];
    if (Find(root, "tables") is { } tablesNode) {
      var tableMap = tablesNode as YamlMappingNode
        ?? throw new SeedFormatException($"{name}: tables must be a mapping of schema.table to changes.");
      foreach (var (key, value) in tableMap.Children) {
        tables.Add(ParseTable(name, Key(name, key), value));
      }
    }
    return new() {
      Name = name,
      Extends = extends,
      Description = description,
      Tables = tables,
    };
  }

  private static TableChange ParseTable(string layer, string table, YamlNode node) {
    if (table.Split('.') is not [{ Length: > 0 }, { Length: > 0 }]) {
      throw new SeedFormatException($"{layer}: '{table}' is not a schema.table name.");
    }
    var map = node as YamlMappingNode
      ?? throw new SeedFormatException($"{layer}: {table} must be a mapping with upsert and delete lists.");
    CheckKeys(layer, table, map, _TableKeys);
    return new() {
      Table = table,
      Upserts = Rows(layer, table, "upsert", map),
      Deletes = Rows(layer, table, "delete", map),
    };
  }

  private static List<Dictionary<string, string?>> Rows(string layer, string table, string key, YamlMappingNode map) {
    if (Find(map, key) is not { } node) {
      return [];
    }
    var sequence = node as YamlSequenceNode
      ?? throw new SeedFormatException($"{layer}: {table}.{key} must be a list of rows.");
    List<Dictionary<string, string?>> rows = new(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var row = item as YamlMappingNode
        ?? throw new SeedFormatException($"{layer}: every {table}.{key} row must be a mapping of column to value.");
      if (row.Children.Count is 0) {
        throw new SeedFormatException($"{layer}: a {table}.{key} row is empty.");
      }
      Dictionary<string, string?> values = new(row.Children.Count, StringComparer.Ordinal);
      foreach (var (column, value) in row.Children) {
        var columnName = Key(layer, column);
        if (!values.TryAdd(columnName, Scalar(layer, $"{table}.{columnName}", value))) {
          throw new SeedFormatException($"{layer}: a {table}.{key} row repeats column '{columnName}'.");
        }
      }
      rows.Add(values);
    }
    return rows;
  }

  private static YamlMappingNode LoadRoot(string name, string yaml) {
    var stream = new YamlStream();
    try {
      using StringReader reader = new(yaml);
      stream.Load(reader);
    } catch (YamlException exception) {
      throw new SeedFormatException($"{name}: {exception.Message}", exception);
    }
    return stream.Documents.Count is 1 && stream.Documents[0].RootNode is YamlMappingNode root
      ? root
      : throw new SeedFormatException($"{name}: the file must hold one YAML mapping.");
  }

  private static void CheckKeys(string layer, string where, YamlMappingNode map, string[] allowed) {
    foreach (var key in map.Children.Keys) {
      var text = Key(layer, key);
      if (!allowed.Contains(text, StringComparer.Ordinal)) {
        throw new SeedFormatException($"{layer}: unknown key '{text}' in {where} (expected {string.Join(", ", allowed)}).");
      }
    }
  }

  private static YamlNode? Find(YamlMappingNode map, string key)
    => map.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: { } value } && string.Equals(value, key, StringComparison.Ordinal)).Value;

  private static YamlNode Required(string layer, YamlMappingNode map, string key)
    => Find(map, key) ?? throw new SeedFormatException($"{layer}: {key} is required.");

  private static string Key(string layer, YamlNode node)
    => node is YamlScalarNode { Value: { Length: > 0 } value }
      ? value
      : throw new SeedFormatException($"{layer}: a mapping key must be plain text.");

  private static string? Scalar(string layer, string where, YamlNode node) {
    if (node is not YamlScalarNode scalar) {
      throw new SeedFormatException($"{layer}: {where} must be a single value.");
    }
    return scalar.Style is ScalarStyle.Plain && scalar.Value is null or "" or "~" or "null"
      ? null
      : scalar.Value;
  }
}
