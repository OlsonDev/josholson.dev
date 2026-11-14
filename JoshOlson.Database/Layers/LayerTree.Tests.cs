using Xunit;

namespace JoshOlson.Database.Layers;

public class LayerTreeTests {
  private static SeedLayer Layer(string name, string? extends = null)
    => new() {
      Name = name,
      Extends = extends,
      Description = name,
      Tables = [],
    };

  private static LayerTree NewTree()
    => new([
      Layer("stress", "base"),
      Layer("minimal", "base"),
      Layer("other"),
      Layer("base"),
      Layer("blog", "minimal"),
    ]);

  [Fact]
  public void Roots_AreTheLayersWithoutAParent() {
    var tree = NewTree();

    Assert.Equal(["base", "other"], tree.Roots.Select(layer => layer.Name));
    Assert.Equal(5, tree.Count);
  }

  [Fact]
  public void ChainTo_StartsAtTheRoot() {
    var tree = NewTree();

    Assert.Equal(["base", "minimal", "blog"], tree.ChainTo("blog").Select(layer => layer.Name));
    Assert.Equal(["other"], tree.ChainTo("other").Select(layer => layer.Name));
    Assert.Equal(2, tree.DepthOf("blog"));
    Assert.Equal(0, tree.DepthOf("base"));
  }

  [Fact]
  public void ChildrenOf_ListsSiblingsByName() {
    var tree = NewTree();

    Assert.Equal(["minimal", "stress"], tree.ChildrenOf("base").Select(layer => layer.Name));
    Assert.Empty(tree.ChildrenOf("blog"));
  }

  [Fact]
  public void InBakeOrder_PutsEveryParentBeforeItsChildren() {
    var tree = NewTree();

    Assert.Equal(["base", "minimal", "blog", "stress", "other"], tree.InBakeOrder().Select(layer => layer.Name));
  }

  [Fact]
  public async Task VisitParentFirstAsync_LayerThatFails_ReportsItAndPassesOverEveryLayerBelowIt() {
    var visited = new List<string>();

    var problems = await NewTree().VisitParentFirstAsync(layer => {
      visited.Add(layer.Name);
      return Task.FromResult(layer.Name is "minimal" ? "minimal: foo" : null);
    });

    Assert.Equal(["base", "minimal", "stress", "other"], visited);
    Assert.Equal(["minimal: foo"], problems);
  }

  [Fact]
  public async Task VisitParentFirstAsync_RootThatFails_PassesOverItsWholeSubtree() {
    var visited = new List<string>();

    var problems = await NewTree().VisitParentFirstAsync(layer => {
      visited.Add(layer.Name);
      return Task.FromResult(layer.Name is "base" ? "base: foo" : null);
    });

    Assert.Equal(["base", "other"], visited);
    Assert.Equal(["base: foo"], problems);
  }

  [Fact]
  public void Constructor_DuplicateName_Throws()
    => Assert.Throws<SeedFormatException>(() => new LayerTree([Layer("foo"), Layer("foo")]));

  [Fact]
  public void Constructor_MissingParent_Throws() {
    var exception = Assert.Throws<SeedFormatException>(() => new LayerTree([Layer("foo", "bar")]));

    Assert.Contains("'bar'", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Constructor_Cycle_Throws() {
    var exception = Assert.Throws<SeedFormatException>(() => new LayerTree([
      Layer("foo", "bar"),
      Layer("bar", "baz"),
      Layer("baz", "foo"),
    ]));

    Assert.Contains("cycle", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Get_UnknownName_Throws()
    => Assert.Throws<SeedFormatException>(() => NewTree().Get("nope"));
}
