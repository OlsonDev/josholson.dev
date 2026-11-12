using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class StateLayerTests {
  [Fact]
  public void Of_AState_MergesBackToTheSameState() {
    var layer = SampleLayers.Layer("base", extends: null, $$"""
        foo.link:
          insert:
            - {{SampleLayers.Link(2, 1, flags: "[1, 2]")}}
            - {{SampleLayers.Link(1, 1, tags: "[3]")}}
        foo.tag:
          insert:
            - {{SampleLayers.Tag(5, price: "1.50")}}
      """);
    var state = SampleLayers.Merge(layer);

    var rebuilt = SampleLayers.Merge(StateLayer.Of(state, "parent"));

    Assert.True(SeedValues.SameState(state, rebuilt));
  }

  [Fact]
  public void Of_AStateWithAnEmptyTable_LeavesThatTableOut() {
    var state = new SeedState();
    state.Tables["foo.tag"] = [with(RowKeyComparer.Instance)];

    Assert.Empty(StateLayer.Of(state, "parent").Tables);
  }
}
