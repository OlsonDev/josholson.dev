using JoshOlson.Database.Layers;
using JoshOlson.Shared.Extensions;
using JoshOlson.Shared.Results;

namespace JoshOlson.Database.Seeding;

// Folds a chain of layers, root first, into the rows they come to (ADR 0014, hierarchical seed layers). In each layer a
// table's deletes apply first, then its inserts, then its updates, so a layer can replace a row by deleting it and
// inserting it again. An insert makes a row that is not there, with every column the parser made it name; an update
// changes only the columns it names, in a row that is there.
//
// Every problem is collected and they are returned together, in place of the rows: a delete or an update of a row that is
// not there, and an insert of one that is (a key two layers both claim, which an overwrite would hide).
public static class LayerMerger {
  public static Result<SeedState, SeedProblems> Merge(List<SeedLayer> chain, SeedSchema schema) {
    var state = new SeedState();
    var problems = new SeedProblems("The layers have");
    foreach (var layer in chain) {
      foreach (var change in layer.Tables) {
        if (schema.Find(change.Table) is not { } table) {
          problems.Add($"{layer.Name}: {change.Table} is not a table in the schema lock.");
          continue;
        }
        if (!state.Tables.TryGetValue(change.Table, out var rows)) {
          state.Tables[change.Table] = rows = [with(RowKeyComparer.Instance)];
        }
        var merge = new TableMerge(layer, change, table, rows, problems);
        merge.Delete();
        merge.Insert();
        merge.Update();
      }
    }
    return problems.Any ? problems : state;
  }

  // One layer's changes to one table, folded into the rows the layers before it came to.
  private sealed record TableMerge(
    SeedLayer Layer,
    TableChange Change,
    SeedTable Table,
    SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>> Rows,
    SeedProblems Problems
  ) {
    public void Delete() {
      foreach (var row in Change.Deletes) {
        var key = KeyOf(row, Table);
        if (!Rows.Remove(key)) {
          Problems.Add($"{Layer.Name}: {Change.Table} deletes the row with key {Describe(key)}, which no layer before it gives.");
        }
      }
    }

    public void Insert() {
      foreach (var row in Change.Inserts) {
        var key = KeyOf(row, Table);
        if (Rows.ContainsKey(key)) {
          Problems.Add($"{Layer.Name}: {Change.Table} inserts the row with key {Describe(key)}, which a layer before it (or this one) already gives.");
          continue;
        }
        Rows[key] = new Dictionary<string, SeedValue?>(row, StringComparer.Ordinal);
      }
    }

    public void Update() {
      foreach (var row in Change.Updates) {
        var key = KeyOf(row, Table);
        if (!Rows.TryGetValue(key, out var merged)) {
          Problems.Add($"{Layer.Name}: {Change.Table} updates the row with key {Describe(key)}, which no layer before it gives.");
          continue;
        }
        foreach (var (column, value) in row) {
          merged[column] = value;
        }
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
