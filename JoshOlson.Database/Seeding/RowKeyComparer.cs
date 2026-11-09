using System.Collections;
using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Seeding;

// Orders rows by primary key, one key column after the next, each by its value: numbers by size, text by ordinal code
// units (so the order never depends on a culture or the database's collation). It also decides when two keys are the same
// row, which is when every part compares equal. Parts of a kind it has no order for compare by their canonical text.
public sealed class RowKeyComparer : IComparer<SeedValue[]>, IComparer {
  public static readonly RowKeyComparer Instance = new();

  public int Compare(SeedValue[]? x, SeedValue[]? y) {
    if (x is null || y is null) {
      return (x is null ? 0 : 1) - (y is null ? 0 : 1);
    }
    for (var i = 0; i < Math.Min(x.Length, y.Length); i++) {
      var result = CompareValues(x[i], y[i]);
      if (result is not 0) {
        return result;
      }
    }
    return x.Length.CompareTo(y.Length);
  }

  int IComparer.Compare(object? x, object? y)
    => Compare(x as SeedValue[], y as SeedValue[]);

  public static int CompareValues(SeedValue left, SeedValue right)
    => (left, right) switch {
      (SeedValue.Integer a, SeedValue.Integer b) => a.Value.CompareTo(b.Value),
      (SeedValue.Decimal a, SeedValue.Decimal b) => a.Value.CompareTo(b.Value),
      (SeedValue.Boolean a, SeedValue.Boolean b) => a.Value.CompareTo(b.Value),
      (SeedValue.RelativeTime a, SeedValue.RelativeTime b) => a.Microseconds.CompareTo(b.Microseconds),
      (SeedValue.AbsoluteTime a, SeedValue.AbsoluteTime b) => a.Value.CompareTo(b.Value),
      (SeedValue.Text a, SeedValue.Text b) => string.CompareOrdinal(a.Value, b.Value),
      _ => string.CompareOrdinal(left.ToString(), right.ToString()),
    };
}
