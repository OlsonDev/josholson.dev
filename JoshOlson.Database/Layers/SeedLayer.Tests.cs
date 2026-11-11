using Xunit;

namespace JoshOlson.Database.Layers;

public class SeedLayerTests {
  [Fact]
  public void RowCount_CountsInsertsUpdatesAndDeletesAcrossTables() {
    var layer = new SeedLayer {
      Name = "foo",
      Extends = null,
      Description = "foo",
      Tables = [
        new() {
          Table = "foo.bar",
          Inserts = [new() { ["bar_id"] = new SeedValue.Integer(1) }, new() { ["bar_id"] = new SeedValue.Integer(2) }],
          Updates = [new() { ["bar_id"] = new SeedValue.Integer(4) }],
          Deletes = [new() { ["bar_id"] = new SeedValue.Integer(3) }],
        },
        new() {
          Table = "foo.baz",
          Inserts = [new() { ["baz_id"] = new SeedValue.Integer(1) }],
          Updates = [],
          Deletes = [],
        },
      ],
    };

    Assert.Equal(5, layer.RowCount);
  }
}
