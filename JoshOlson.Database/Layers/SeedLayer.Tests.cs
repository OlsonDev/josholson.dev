using Xunit;

namespace JoshOlson.Database.Layers;

public class SeedLayerTests {
  [Fact]
  public void RowCount_CountsUpsertsAndDeletesAcrossTables() {
    var layer = new SeedLayer {
      Name = "foo",
      Extends = null,
      Description = "foo",
      Tables = [
        new() {
          Table = "foo.bar",
          Upserts = [new() { ["bar_id"] = new SeedValue.Integer(1) }, new() { ["bar_id"] = new SeedValue.Integer(2) }],
          Deletes = [new() { ["bar_id"] = new SeedValue.Integer(3) }],
        },
        new() {
          Table = "foo.baz",
          Upserts = [new() { ["baz_id"] = new SeedValue.Integer(1) }],
          Deletes = [],
        },
      ],
    };

    Assert.Equal(4, layer.RowCount);
  }
}
