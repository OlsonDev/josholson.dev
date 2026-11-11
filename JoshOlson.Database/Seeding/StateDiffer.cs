using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Seeding;

// What a layer would have to do to turn one state into another, which is how an export makes a layer from a database
// (ADR 0014, hierarchical seed layers). Rows are matched by primary key, and each table that differs gets:
// - an `insert` of every column of a row only the new state has;
// - an `update` of the key and only the columns that changed, for a row both have;
// - a `delete` of the key of a row only the old state has.
// A row that did not change is left out, and so is a table with no change.
//
// It works on states read the same way (`SeedStateReader`), so a relative time on each side is an offset from its own
// database's anchor and the same row compares equal whatever the two anchors are.
public static class StateDiffer {
  public static List<TableChange> Diff(SeedState parent, SeedState child, SeedSchema schema) {
    var changes = new List<TableChange>();
    foreach (var name in parent.Tables.Keys.Union(child.Tables.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)) {
      var table = schema.Find(name) ?? throw new SeedFormatException($"{name} is not a table in the schema lock.");
      var before = parent.Tables.GetValueOrDefault(name) ?? Empty();
      var after = child.Tables.GetValueOrDefault(name) ?? Empty();
      var change = new TableChange {
        Table = name,
        Inserts = [],
        Updates = [],
        Deletes = [],
      };
      foreach (var (key, row) in after) {
        if (!before.TryGetValue(key, out var was)) {
          change.Inserts.Add(new(row, StringComparer.Ordinal));
        } else if (Update(table, row, was) is { } update) {
          change.Updates.Add(update);
        }
      }
      foreach (var key in before.Keys.Where(key => !after.ContainsKey(key))) {
        change.Deletes.Add(KeyRow(table, key));
      }
      if (change.Inserts.Count + change.Updates.Count + change.Deletes.Count > 0) {
        changes.Add(change);
      }
    }
    return changes;
  }

  private static SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>> Empty()
    => [with(RowKeyComparer.Instance)];

  // The key and the columns whose value is not what it was, or null when none is.
  private static Dictionary<string, SeedValue?>? Update(SeedTable table, Dictionary<string, SeedValue?> row, Dictionary<string, SeedValue?> was) {
    var changed = row.Where(cell => !was.TryGetValue(cell.Key, out var old) || !SeedValues.Same(old, cell.Value)).ToList();
    if (changed.Count is 0) {
      return null;
    }
    var update = new Dictionary<string, SeedValue?>(StringComparer.Ordinal);
    foreach (var column in table.Table.PrimaryKey) {
      update[column] = row[column];
    }
    foreach (var (column, value) in changed) {
      update[column] = value;
    }
    return update;
  }

  private static Dictionary<string, SeedValue?> KeyRow(SeedTable table, SeedValue[] key) {
    var row = new Dictionary<string, SeedValue?>(key.Length, StringComparer.Ordinal);
    for (var i = 0; i < key.Length; i++) {
      row[table.Table.PrimaryKey[i]] = key[i];
    }
    return row;
  }
}
