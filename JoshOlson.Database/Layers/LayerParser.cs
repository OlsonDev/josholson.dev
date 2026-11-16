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

    var problems = new SeedProblems($"{name} has");
    var tables = new List<TableChange>();
    if (YamlNodes.Find(root, "tables") is { } tablesNode) {
      var tableMap = tablesNode as YamlMappingNode
        ?? throw new SeedFormatException($"{name}: tables must be a mapping of schema.table to changes.");
      foreach (var (key, value) in tableMap.Children) {
        tables.Add(ParseTable(name, YamlNodes.Key(name, key), value, schema, problems));
      }
    }
    problems.ThrowIfAny();
    return new() {
      Name = name,
      Extends = extends,
      Description = description,
      Tables = tables,
    };
  }

  private static TableChange ParseTable(string layer, string table, YamlNode node, SeedSchema schema, SeedProblems problems) {
    if (table.Split('.') is not [{ Length: > 0 }, { Length: > 0 }]) {
      throw new SeedFormatException($"{layer}: '{table}' is not a schema.table name.");
    }
    var map = node as YamlMappingNode
      ?? throw new SeedFormatException($"{layer}: {table} must be a mapping with insert, update, and delete lists.");
    YamlNodes.CheckKeys(layer, table, map, _TableKeys);

    var known = schema.Find(table);
    if (known is null) {
      problems.Add($"{layer}: {table} is not a table in the schema lock.");
    } else if (known.Table.Ignored) {
      problems.Add($"{layer}: {table} holds runtime data (its comment says @seed ignore), not seed data, so a layer cannot name it.");
    } else if (known.Table.PrimaryKey.Count is 0) {
      problems.Add($"{layer}: {table} has no primary key, and a layer finds its rows by it.");
    }
    var reader = new TableReader(layer, table, known, problems);
    return new() {
      Table = table,
      Inserts = reader.Rows("insert", map),
      Updates = reader.Rows("update", map),
      Deletes = reader.Rows("delete", map),
    };
  }

  // One table's rows in one layer, read as the table's columns when the lock knows the table.
  private sealed record TableReader(string Layer, string Table, SeedTable? Known, SeedProblems Problems) {
    public List<Dictionary<string, SeedValue?>> Rows(string key, YamlMappingNode map) {
      if (YamlNodes.Find(map, key) is not { } node) {
        return [];
      }
      var sequence = node as YamlSequenceNode
        ?? throw new SeedFormatException($"{Layer}: {Table}.{key} must be a list of rows.");
      var rows = new List<Dictionary<string, SeedValue?>>(sequence.Children.Count);
      foreach (var item in sequence.Children) {
        var row = item as YamlMappingNode
          ?? throw new SeedFormatException($"{Layer}: every {Table}.{key} row must be a mapping of column to value.");
        if (row.Children.Count is 0) {
          throw new SeedFormatException($"{Layer}: a {Table}.{key} row is empty.");
        }
        var where = $"{Layer}: {Table} {key} row {(rows.Count + 1).ToInvariantString()}";
        var values = new Dictionary<string, SeedValue?>(row.Children.Count, StringComparer.Ordinal);
        foreach (var (column, value) in row.Children) {
          var columnName = YamlNodes.Key(Layer, column);
          if (values.ContainsKey(columnName)) {
            throw new SeedFormatException($"{Layer}: a {Table}.{key} row repeats column '{columnName}'.");
          }
          values[columnName] = ReadValue(where, columnName, value, key is not "delete");
        }
        if (Known is { } known) {
          CheckKey(where, values, known);
          switch (key) {
            case "insert":
              CheckComplete(where, values, known);
              break;
            case "update":
              CheckChanges(where, values, known);
              break;
            default:
              CheckOnlyKey(where, values, known);
              break;
          }
        }
        rows.Add(values);
      }
      return rows;
    }

    // The value as the column's type, or null with a problem noted when it is not (or when the column is unknown, which
    // is noted too, so the one fault is reported once).
    private SeedValue? ReadValue(string where, string column, YamlNode node, bool isSet) {
      if (Known is not { } known) {
        return null;
      }
      if (!known.Columns.TryGetValue(column, out var seedColumn)) {
        Problems.Add($"{where}: {column} is not a column of {known.Table.Name}.");
        return null;
      }
      if (isSet && seedColumn.Column.GeneratedAs is GeneratedAs.ExpressionStored or GeneratedAs.ExpressionVirtual) {
        Problems.Add($"{where}: {column} is generated from an expression, so a seed cannot set it.");
        return null;
      }
      if (SeedValueReader.Read(seedColumn, node, out var value) is { } problem) {
        Problems.Add($"{where}: {column} {problem}");
        return null;
      }
      return value;
    }

    private void CheckKey(string where, Dictionary<string, SeedValue?> values, SeedTable known) {
      foreach (var column in known.Table.PrimaryKey) {
        if (!values.ContainsKey(column)) {
          Problems.Add($"{where}: the primary key column {column} is missing.");
        }
      }
    }

    // An insert leaves nothing to a default, which a later migration could change under the seed (ADR 0014, hierarchical seed
    // layers): it names every column that can be set, a NULL one as `~`.
    private void CheckComplete(string where, Dictionary<string, SeedValue?> values, SeedTable known) {
      var missing = known.Table.Columns
        .Where(column => column.GeneratedAs is not (GeneratedAs.ExpressionStored or GeneratedAs.ExpressionVirtual))
        .Select(column => column.Name)
        .Where(column => !values.ContainsKey(column))
        .ToList();
      if (missing.Count > 0) {
        Problems.Add($"{where}: an insert names every column, and it leaves out {string.Join(", ", missing)}.");
      }
    }

    private void CheckChanges(string where, Dictionary<string, SeedValue?> values, SeedTable known) {
      if (values.Keys.All(column => known.Table.PrimaryKey.Contains(column) || !known.Columns.ContainsKey(column))) {
        Problems.Add($"{where}: an update names the key and the columns it changes, and it changes none.");
      }
    }

    private void CheckOnlyKey(string where, Dictionary<string, SeedValue?> values, SeedTable known) {
      foreach (var column in values.Keys.Where(column => known.Columns.ContainsKey(column) && !known.Table.PrimaryKey.Contains(column))) {
        Problems.Add($"{where}: a delete holds only the primary key, so {column} does not belong.");
      }
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
