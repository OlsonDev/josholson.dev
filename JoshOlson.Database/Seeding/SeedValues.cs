using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Seeding;

// Whether two values are the same value. A `SeedValue` is a record, so its equality is by value everywhere but an array,
// whose items are a list that a record compares by reference.
public static class SeedValues {
  public static bool Same(SeedValue? left, SeedValue? right)
    => (left, right) switch {
      (SeedValue.Array a, SeedValue.Array b) => a.Items.Count == b.Items.Count && a.Items.Zip(b.Items).All(pair => Same(pair.First, pair.Second)),
      _ => Equals(left, right),
    };

  // Whether two rows hold the same columns with the same values.
  public static bool SameRow(Dictionary<string, SeedValue?> left, Dictionary<string, SeedValue?> right)
    => left.Count == right.Count && left.All(cell => right.TryGetValue(cell.Key, out var other) && Same(cell.Value, other));

  // Whether two states hold the same tables, each with the same rows.
  public static bool SameState(SeedState left, SeedState right) {
    var names = left.Tables.Keys.Union(right.Tables.Keys, StringComparer.Ordinal);
    foreach (var name in names) {
      var a = left.Tables.GetValueOrDefault(name);
      var b = right.Tables.GetValueOrDefault(name);
      if ((a?.Count ?? 0) != (b?.Count ?? 0)) {
        return false;
      }
      if (a is not null && b is not null && !a.All(row => b.TryGetValue(row.Key, out var other) && SameRow(row.Value, other))) {
        return false;
      }
    }
    return true;
  }
}
