using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class SeedValuesTests {
  private static SeedValue.Array Array(params long?[] items)
    => new([.. items.Select(item => item is { } value ? new SeedValue.Integer(value) : (SeedValue?)null)]);

  [Fact]
  public void Same_ArraysWithTheSameItems_AreTheSame()
    => Assert.True(SeedValues.Same(Array(1, null, 3), Array(1, null, 3)));

  [Fact]
  public void Same_ArraysThatDiffer_AreNot() {
    Assert.False(SeedValues.Same(Array(1, 2), Array(1, 3)));
    Assert.False(SeedValues.Same(Array(1, 2), Array(1)));
    Assert.False(SeedValues.Same(Array(), Array(0)));
    Assert.False(SeedValues.Same(Array(1, null), Array(1, 0)));
  }

  [Fact]
  public void Same_NullAndAValue_AreNotTheSame() {
    Assert.False(SeedValues.Same(left: null, new SeedValue.Text("foo")));
    Assert.False(SeedValues.Same(new SeedValue.Text("foo"), right: null));
    Assert.True(SeedValues.Same(left: null, right: null));
  }

  [Fact]
  public void Same_AnArrayAndAScalar_AreNot()
    => Assert.False(SeedValues.Same(Array(1), new SeedValue.Integer(1)));

  [Fact]
  public void SameRow_NeedsTheSameColumnsAndValues() {
    Dictionary<string, SeedValue?> row = new() {
      ["a"] = new SeedValue.Integer(1),
      ["b"] = Array(1),
    };

    Assert.True(SeedValues.SameRow(row, new() { ["a"] = new SeedValue.Integer(1), ["b"] = Array(1) }));
    Assert.False(SeedValues.SameRow(row, new() { ["a"] = new SeedValue.Integer(1) }));
    Assert.False(SeedValues.SameRow(row, new() { ["a"] = new SeedValue.Integer(2), ["b"] = Array(1) }));
    Assert.False(SeedValues.SameRow(row, new() { ["a"] = new SeedValue.Integer(1), ["c"] = Array(1) }));
  }

  [Fact]
  public void SameState_TablesAndRowsCompareByContent() {
    var left = new SeedState();
    left.Tables["foo.bar"] = [with(RowKeyComparer.Instance)];
    left.Tables["foo.bar"][[new SeedValue.Integer(1)]] = new() { ["bar_id"] = new SeedValue.Integer(1) };
    var right = new SeedState();
    right.Tables["foo.bar"] = [with(RowKeyComparer.Instance)];
    right.Tables["foo.bar"][[new SeedValue.Integer(1)]] = new() { ["bar_id"] = new SeedValue.Integer(1) };

    Assert.True(SeedValues.SameState(left, right));

    right.Tables["foo.bar"][[new SeedValue.Integer(2)]] = new() { ["bar_id"] = new SeedValue.Integer(2) };

    Assert.False(SeedValues.SameState(left, right));
  }

  [Fact]
  public void SameState_ATableThatIsMissingAndOneThatIsEmpty_AreTheSame() {
    var left = new SeedState();
    var right = new SeedState();
    right.Tables["foo.bar"] = [with(RowKeyComparer.Instance)];

    Assert.True(SeedValues.SameState(left, right));
  }
}
