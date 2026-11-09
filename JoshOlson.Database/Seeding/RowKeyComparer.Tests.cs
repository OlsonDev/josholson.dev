using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class RowKeyComparerTests {
  [Fact]
  public void Compare_Numbers_BySizeNotText()
    => Assert.True(RowKeyComparer.Instance.Compare([new SeedValue.Integer(9)], [new SeedValue.Integer(10)]) < 0);

  [Fact]
  public void Compare_Text_ByOrdinalCodeUnits()
    => Assert.True(RowKeyComparer.Instance.Compare([new SeedValue.Text("B")], [new SeedValue.Text("a")]) < 0);

  [Fact]
  public void Compare_CompositeKey_FirstDifferingPartDecides() {
    var left = new SeedValue[] {
      new SeedValue.Integer(1),
      new SeedValue.Integer(9),
    };
    var right = new SeedValue[] {
      new SeedValue.Integer(1),
      new SeedValue.Integer(10),
    };

    Assert.True(RowKeyComparer.Instance.Compare(left, right) < 0);
    Assert.Equal(0, RowKeyComparer.Instance.Compare(left, left));
  }
}
