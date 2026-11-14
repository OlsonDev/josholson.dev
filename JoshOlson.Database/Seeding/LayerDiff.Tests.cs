using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Database.Seeding;

public sealed class LayerDiffTests {
  private static SeedLayer Base()
    => SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "One")}}
            - {{SampleLayers.Bar(2, "Two")}}
      """);

  [Fact]
  public void Between_StateWithChanges_IsTheLayerItsTextAndBothChecksPassed() {
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1001, "Three")}}
          update:
            - { bar_id: 2, name: Deux }
          delete:
            - { bar_id: 1 }
      """);
    var baseline = SampleLayers.Merge(Base());
    var state = SampleLayers.Merge(Base(), child);

    var diff = LayerDiff.Between(baseline, state, "foo", "base", "Foo bar", SampleLayers.Schema);

    Assert.NotNull(diff);
    Assert.Equal("foo", diff.Layer.Name);
    Assert.Equal("base", diff.Layer.Extends);
    Assert.Equal("Foo bar", diff.Layer.Description);
    Assert.True(diff.GivesBackTheState);
    Assert.True(diff.ReadsBack);
    Assert.Equal(
      $$"""
      description: Foo bar
      extends: base
      tables:
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1001, "Three")}}
          update:
            - { bar_id: 2, name: Deux }
          delete:
            - { bar_id: 1 }

      """,
      diff.Yaml);
  }

  [Fact]
  public void Between_TheSameRows_IsNoLayer() {
    var state = SampleLayers.Merge(Base());

    var diff = LayerDiff.Between(state, SampleLayers.Merge(Base()), "foo", extends: null, "Foo bar", SampleLayers.Schema);

    Assert.Null(diff);
  }
}
