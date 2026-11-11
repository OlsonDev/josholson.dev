using JoshOlson.Database.Layers;
using JoshOlson.Shared.Extensions;

namespace JoshOlson.Database.Seeding;

// Folds a chain of layers, root first, into the rows they come to (ADR 0014, hierarchical seed layers). In each layer a
// table's deletes apply first, then its inserts, then its updates, so a layer can replace a row by deleting it and
// inserting it again. An insert makes a row that is not there, with every column the parser made it name; an update
// changes only the columns it names, in a row that is there.
//
// Every problem is collected and thrown together: a delete or an update of a row that is not there, and an insert of one
// that is (a key two layers both claim, which an overwrite would hide).
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
          state.Tables[change.Table] = rows = [with(RowKeyComparer.Instance)];
        }
        Delete(layer, change, table, rows, problems);
        Insert(layer, change, table, rows, problems);
        Update(layer, change, table, rows, problems);
      }
    }
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

  private static void Insert(
    SeedLayer layer,
    TableChange change,
    SeedTable table,
    SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>> rows,
    List<string> problems
  ) {
    foreach (var row in change.Inserts) {
      var key = KeyOf(row, table);
      if (rows.ContainsKey(key)) {
        problems.Add($"{layer.Name}: {change.Table} inserts the row with key {Describe(key)}, which a layer before it (or this one) already gives.");
        continue;
      }
      rows[key] = new Dictionary<string, SeedValue?>(row, StringComparer.Ordinal);
    }
  }

  private static void Update(
    SeedLayer layer,
    TableChange change,
    SeedTable table,
    SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>> rows,
    List<string> problems
  ) {
    foreach (var row in change.Updates) {
      var key = KeyOf(row, table);
      if (!rows.TryGetValue(key, out var merged)) {
        problems.Add($"{layer.Name}: {change.Table} updates the row with key {Describe(key)}, which no layer before it gives.");
        continue;
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

  private static string Describe(SeedValue[] key)
    => string.Join(", ", key.Select(DescribeValue));

  private static string DescribeValue(SeedValue value)
    => value switch {
      SeedValue.Integer integer => integer.Value.ToInvariantString(),
      SeedValue.Text text => text.Value,
      _ => value.ToString() ?? "",
    };
}
