using JoshOlson.Database.Yaml;
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
    var root = YamlNodes.LoadRoot(name, yaml);
    YamlNodes.CheckKeys(name, "the layer", root, _LayerKeys);

    var description = Scalar(name, "description", YamlNodes.Required(name, root, "description"));
    if (string.IsNullOrWhiteSpace(description)) {
      throw new SeedFormatException($"{name}: description is empty.");
    }
    string? extends = null;
    if (YamlNodes.Find(root, "extends") is { } extendsNode) {
      extends = Scalar(name, "extends", extendsNode);
      if (extends is null || !LayerName.IsValid(extends)) {
        throw new SeedFormatException($"{name}: extends '{extends}' is not a layer name.");
      }
    }

    List<TableChange> tables = [];
    if (YamlNodes.Find(root, "tables") is { } tablesNode) {
      var tableMap = tablesNode as YamlMappingNode
        ?? throw new SeedFormatException($"{name}: tables must be a mapping of schema.table to changes.");
      foreach (var (key, value) in tableMap.Children) {
        tables.Add(ParseTable(name, YamlNodes.Key(name, key), value));
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
    YamlNodes.CheckKeys(layer, table, map, _TableKeys);
    return new() {
      Table = table,
      Upserts = Rows(layer, table, "upsert", map),
      Deletes = Rows(layer, table, "delete", map),
    };
  }

  private static List<Dictionary<string, string?>> Rows(string layer, string table, string key, YamlMappingNode map) {
    if (YamlNodes.Find(map, key) is not { } node) {
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
        var columnName = YamlNodes.Key(layer, column);
        if (!values.TryAdd(columnName, Scalar(layer, $"{table}.{columnName}", value))) {
          throw new SeedFormatException($"{layer}: a {table}.{key} row repeats column '{columnName}'.");
        }
      }
      rows.Add(values);
    }
    return rows;
  }

  // A layer value: a plain `~`, `null`, or empty value is NULL, and a quoted one is the text.
  private static string? Scalar(string layer, string where, YamlNode node) {
    if (node is not YamlScalarNode scalar) {
      throw new SeedFormatException($"{layer}: {where} must be a single value.");
    }
    return scalar.Style is ScalarStyle.Plain && scalar.Value is null or "" or "~" or "null"
      ? null
      : scalar.Value;
  }
}
