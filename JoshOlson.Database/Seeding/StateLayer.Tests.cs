using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class StateLayerTests {
  private static readonly SeedSchema _Schema = new(SchemaLockSample.Create());

  [Fact]
  public void Of_AState_MergesBackToTheSameState() {
    const string Yaml = """
      description: Base
      tables:
        foo.link:
          insert:
            - { bar_id: 2, tag_id: 1, flags: [1, 2], tags: ~ }
            - { bar_id: 1, tag_id: 1, flags: ~, tags: [3] }
        foo.tag:
          insert:
            - { tag_id: 5, price: 1.50 }
      """;
    var state = LayerMerger.Merge([LayerParser.Parse("base", Yaml, _Schema)], _Schema);

    var rebuilt = LayerMerger.Merge([StateLayer.Of(state, "parent")], _Schema);

    Assert.True(SeedValues.SameState(state, rebuilt));
  }

  [Fact]
  public void Of_AStateWithAnEmptyTable_LeavesThatTableOut() {
    var state = new SeedState();
    state.Tables["foo.tag"] = [with(RowKeyComparer.Instance)];

    Assert.Empty(StateLayer.Of(state, "parent").Tables);
  }
}
