using JoshOlson.Database.Schema;
using JoshOlson.Database.Yaml;
using JoshOlson.Shared.Extensions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace JoshOlson.Database.Layers;

// Reads one layer file:
//
//   description: What this layer adds, in a sentence
//   extends: parent-layer        # omit for a root layer
//   tables:
//     foo.bar:                   # schema.table
//       insert:                  # new rows: every column that is not generated from an expression
//         - { bar_id: 1001, name: baz, qux: ~ }
//       update:                  # rows an earlier layer gives: the key and the columns that change
//         - { bar_id: 1, name: quux }
//       delete:
//         - { bar_id: 2 }        # primary key columns only
//
// Each value is read as its column's type from the lock (`SeedValueReader`), and a plain `~`, `null`, or empty value is
// NULL. A file that is not the shape above throws at the first fault. Once it is, every problem with its tables and
// values (an unknown table or column, a value that is not its column's type, a missing key column, an insert that leaves
// a column out, an update that changes nothing) is collected, and they are thrown together. Whether an insert's key is new
// and an update's is not is for `LayerMerger`, which sees the layers before it.
public static class LayerParser {
  private static readonly string[] _LayerKeys = [
    "description",
    "extends",
    "tables",
  ];
  private static readonly string[] _TableKeys = [
    "insert",
    "update",
    "delete",
  ];

  public static SeedLayer Parse(string name, string yaml, SeedSchema schema) {
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

    var problems = new List<string>();
    var tables = new List<TableChange>();
    if (YamlNodes.Find(root, "tables") is { } tablesNode) {
      var tableMap = tablesNode as YamlMappingNode
        ?? throw new SeedFormatException($"{name}: tables must be a mapping of schema.table to changes.");
      foreach (var (key, value) in tableMap.Children) {
        tables.Add(ParseTable(name, YamlNodes.Key(name, key), value, schema, problems));
      }
    }
    if (problems.Count > 0) {
      throw new SeedFormatException(problems.Count is 1
        ? problems[0]
        : $"{name} has {problems.Count.ToInvariantString()} problems:\n- {string.Join("\n- ", problems)}");
    }
    return new() {
      Name = name,
      Extends = extends,
      Description = description,
      Tables = tables,
    };
  }

  private static TableChange ParseTable(string layer, string table, YamlNode node, SeedSchema schema, List<string> problems) {
    if (table.Split('.') is not [{ Length: > 0 }, { Length: > 0 }]) {
      throw new SeedFormatException($"{layer}: '{table}' is not a schema.table name.");
    }
    var map = node as YamlMappingNode
      ?? throw new SeedFormatException($"{layer}: {table} must be a mapping with insert, update, and delete lists.");
    YamlNodes.CheckKeys(layer, table, map, _TableKeys);

    var known = schema.Find(table);
    if (known is null) {
      problems.Add($"{layer}: {table} is not a table in the schema lock.");
    } else if (known.Table.PrimaryKey.Count is 0) {
      problems.Add($"{layer}: {table} has no primary key, and a layer finds its rows by it.");
    }
    return new() {
      Table = table,
      Inserts = Rows(layer, table, "insert", map, known, problems),
      Updates = Rows(layer, table, "update", map, known, problems),
      Deletes = Rows(layer, table, "delete", map, known, problems),
    };
  }

  private static List<Dictionary<string, SeedValue?>> Rows(
    string layer,
    string table,
    string key,
    YamlMappingNode map,
    SeedTable? known,
    List<string> problems
  ) {
    if (YamlNodes.Find(map, key) is not { } node) {
      return [];
    }
    var sequence = node as YamlSequenceNode
      ?? throw new SeedFormatException($"{layer}: {table}.{key} must be a list of rows.");
    var rows = new List<Dictionary<string, SeedValue?>>(sequence.Children.Count);
    foreach (var item in sequence.Children) {
      var row = item as YamlMappingNode
        ?? throw new SeedFormatException($"{layer}: every {table}.{key} row must be a mapping of column to value.");
      if (row.Children.Count is 0) {
        throw new SeedFormatException($"{layer}: a {table}.{key} row is empty.");
      }
      var where = $"{layer}: {table} {key} row {(rows.Count + 1).ToInvariantString()}";
      var values = new Dictionary<string, SeedValue?>(row.Children.Count, StringComparer.Ordinal);
      foreach (var (column, value) in row.Children) {
        var columnName = YamlNodes.Key(layer, column);
        if (values.ContainsKey(columnName)) {
          throw new SeedFormatException($"{layer}: a {table}.{key} row repeats column '{columnName}'.");
        }
        values[columnName] = ReadValue(where, columnName, value, known, key is not "delete", problems);
      }
      if (known is not null) {
        CheckKey(where, values, known, problems);
        switch (key) {
          case "insert":
            CheckComplete(where, values, known, problems);
            break;
          case "update":
            CheckChanges(where, values, known, problems);
            break;
          default:
            CheckOnlyKey(where, values, known, problems);
            break;
        }
      }
      rows.Add(values);
    }
    return rows;
  }

  // The value as the column's type, or null with a problem noted when it is not (or when the column is unknown, which
  // is noted too, so the one fault is reported once).
  private static SeedValue? ReadValue(
    string where,
    string column,
    YamlNode node,
    SeedTable? known,
    bool isSet,
    List<string> problems
  ) {
    if (known is null) {
      return null;
    }
    if (!known.Columns.TryGetValue(column, out var seedColumn)) {
      problems.Add($"{where}: {column} is not a column of {known.Table.Name}.");
      return null;
    }
    if (isSet && seedColumn.Column.GeneratedAs is GeneratedAs.ExpressionStored or GeneratedAs.ExpressionVirtual) {
      problems.Add($"{where}: {column} is generated from an expression, so a seed cannot set it.");
      return null;
    }
    if (SeedValueReader.Read(seedColumn, node, out var value) is { } problem) {
      problems.Add($"{where}: {column} {problem}");
      return null;
    }
    return value;
  }

  private static void CheckKey(string where, Dictionary<string, SeedValue?> values, SeedTable known, List<string> problems) {
    foreach (var column in known.Table.PrimaryKey) {
      if (!values.ContainsKey(column)) {
        problems.Add($"{where}: the primary key column {column} is missing.");
      }
    }
  }

  // An insert leaves nothing to a default, which a later migration could change under the seed (ADR 0014, hierarchical seed
  // layers): it names every column that can be set, a NULL one as `~`.
  private static void CheckComplete(string where, Dictionary<string, SeedValue?> values, SeedTable known, List<string> problems) {
    var missing = known.Table.Columns
      .Where(column => column.GeneratedAs is not (GeneratedAs.ExpressionStored or GeneratedAs.ExpressionVirtual))
      .Select(column => column.Name)
      .Where(column => !values.ContainsKey(column))
      .ToList();
    if (missing.Count > 0) {
      problems.Add($"{where}: an insert names every column, and it leaves out {string.Join(", ", missing)}.");
    }
  }

  private static void CheckChanges(string where, Dictionary<string, SeedValue?> values, SeedTable known, List<string> problems) {
    if (values.Keys.All(column => known.Table.PrimaryKey.Contains(column) || !known.Columns.ContainsKey(column))) {
      problems.Add($"{where}: an update names the key and the columns it changes, and it changes none.");
    }
  }

  private static void CheckOnlyKey(string where, Dictionary<string, SeedValue?> values, SeedTable known, List<string> problems) {
    foreach (var column in values.Keys.Where(column => known.Columns.ContainsKey(column) && !known.Table.PrimaryKey.Contains(column))) {
      problems.Add($"{where}: a delete holds only the primary key, so {column} does not belong.");
    }
  }

  // A header value (`description`, `extends`): a plain `~`, `null`, or empty value is null, and a quoted one is the text.
  private static string? Scalar(string layer, string where, YamlNode node) {
    if (node is not YamlScalarNode scalar) {
      throw new SeedFormatException($"{layer}: {where} must be a single value.");
    }
    return scalar.Style is ScalarStyle.Plain && scalar.Value is null or "" or "~" or "null"
      ? null
      : scalar.Value;
  }
}
