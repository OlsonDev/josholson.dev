using JoshOlson.Database.Layers;
using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Seeding;

// Folds a chain of layers, root first, into the rows they come to (ADR 0014, hierarchical seed layers). In each layer a
// table's deletes apply before its upserts, so a layer can replace a row by deleting and giving it again. An upsert
// to a row that exists changes only the columns it names; one to a row that does not makes it.
//
// Every problem is collected and thrown together: a delete of a row that is not there, and, in the finished rows, a
// column that is NOT NULL, has no default, and that no layer gave a value, which would fail the insert. That second check
// is why it runs on the whole chain: a layer may name only the columns it changes.
public static class LayerMerger {
  public static SeedState Merge(List<SeedLayer> chain, SeedSchema schema) {
    var state = new SeedState();
    var problems = new List<string>();
    foreach (var layer in chain) {
      foreach (var change in layer.Tables) {
        if (schema.Find(change.Table) is not { } table) {
          problems.Add($"{layer.Name}: {change.Table} is not a table in the schema lock.");
          continue;
        }
        if (!state.Tables.TryGetValue(change.Table, out var rows)) {
          state.Tables[change.Table] = rows = new(RowKeyComparer.Instance);
        }
        Delete(layer, change, table, rows, problems);
        Upsert(change, table, rows);
      }
    }
    CheckRequired(state, schema, problems);
    if (problems.Count > 0) {
      throw new SeedFormatException(problems.Count is 1
        ? problems[0]
        : $"The layers have {problems.Count.ToInvariantString()} problems:\n- {string.Join("\n- ", problems)}");
    }
    return state;
  }

  private static void Delete(
    SeedLayer layer,
    TableChange change,
    SeedTable table,
    SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>> rows,
    List<string> problems
  ) {
    foreach (var row in change.Deletes) {
      var key = KeyOf(row, table);
      if (!rows.Remove(key)) {
        problems.Add($"{layer.Name}: {change.Table} deletes the row with key {Describe(key)}, which no layer before it gives.");
      }
    }
  }

  private static void Upsert(
    TableChange change,
    SeedTable table,
    SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>> rows
  ) {
    foreach (var row in change.Upserts) {
      var key = KeyOf(row, table);
      if (!rows.TryGetValue(key, out var merged)) {
        rows[key] = merged = new(StringComparer.Ordinal);
      }
      foreach (var (column, value) in row) {
        merged[column] = value;
      }
    }
  }

  // The parser has already made sure every key column is there and not NULL.
  private static SeedValue[] KeyOf(Dictionary<string, SeedValue?> row, SeedTable table) {
    var key = new SeedValue[table.Table.PrimaryKey.Count];
    for (var i = 0; i < key.Length; i++) {
      key[i] = row[table.Table.PrimaryKey[i]]
        ?? throw new SeedFormatException($"{table.Table.Name} has a row whose key column {table.Table.PrimaryKey[i]} is NULL.");
    }
    return key;
  }

  private static void CheckRequired(SeedState state, SeedSchema schema, List<string> problems) {
    foreach (var (name, rows) in state.Tables.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
      var required = schema.Find(name)!.Table.Columns
        .Where(column => !column.Nullable && column.DefaultExpression is null && column.GeneratedAs is null)
        .Select(column => column.Name)
        .ToList();
      foreach (var (key, row) in rows) {
        foreach (var column in required.Where(column => !row.ContainsKey(column))) {
          problems.Add($"{name} row {Describe(key)}: {column} is NOT NULL and has no default, and no layer gives it a value.");
        }
      }
    }
  }

  private static string Describe(SeedValue[] key)
    => string.Join(", ", key.Select(DescribeValue));

  private static string DescribeValue(SeedValue value)
    => value switch {
      SeedValue.Integer integer => integer.Value.ToInvariantString(),
      SeedValue.Text text => text.Value,
      _ => value.ToString() ?? "",
    };
}
