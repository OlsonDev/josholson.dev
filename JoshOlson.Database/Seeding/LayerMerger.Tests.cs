using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class LayerMergerTests {
  private static readonly SeedSchema _Schema = new(SchemaLockSample.Create());

  // A `foo.bar` insert, which names every column that is not generated from an expression.
  private static string Bar(int id, string slug, string name, string note = "~", string lat = "1.5")
    => $"{{ bar_id: {id.ToInvariantString()}, slug: {slug}, name: {name}, note: {note}, created_utc: 0s, published_utc: ~, lat: {lat} }}";

  [Fact]
  public void Merge_ChildChangesOnlyTheColumnsItNames() {
    var @base = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "one", "First")}}
      """;
    var child = $$"""
      description: Child
      extends: base
      tables:
        foo.bar:
          insert:
            - {{Bar(2, "two", "Second", lat: "2.5")}}
          update:
            - { bar_id: 1, name: Renamed }
      """;

    var state = Merge(("base", @base), ("child", child));

    var rows = state.Tables["foo.bar"].Values.ToList();
    Assert.Equal(2, rows.Count);
    Assert.Equal(new SeedValue.Text("Renamed"), rows[0]["name"]);
    Assert.Equal(new SeedValue.Text("one"), rows[0]["slug"]);
    Assert.Equal(new SeedValue.Text("Second"), rows[1]["name"]);
    Assert.Equal(2, state.RowCount);
  }

  [Fact]
  public void Merge_ExplicitNull_ReplacesTheValue() {
    var @base = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "one", "First", note: "hello")}}
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          update:
            - { bar_id: 1, note: ~ }
      """;

    var state = Merge(("base", @base), ("child", Child));

    Assert.Null(state.Tables["foo.bar"].Values.Single()["note"]);
  }

  [Fact]
  public void Merge_DeleteRemovesTheRow() {
    var @base = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "one", "First")}}
            - {{Bar(2, "two", "Second", lat: "2.5")}}
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          delete:
            - { bar_id: 1 }
      """;

    var state = Merge(("base", @base), ("child", Child));

    Assert.Equal(new SeedValue.Text("two"), state.Tables["foo.bar"].Values.Single()["slug"]);
  }

  [Fact]
  public void Merge_InOneLayer_DeletesApplyBeforeInserts() {
    var @base = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "one", "First", note: "old")}}
      """;
    var child = $$"""
      description: Child
      extends: base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "uno", "Again", lat: "3.5")}}
          delete:
            - { bar_id: 1 }
      """;

    var row = Merge(("base", @base), ("child", child)).Tables["foo.bar"].Values.Single();

    Assert.Equal(new SeedValue.Text("uno"), row["slug"]);
    Assert.Null(row["note"]);
  }

  [Fact]
  public void Merge_RowsComeOutInKeyOrder() {
    var @base = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(10, "ten", "Ten", lat: "1")}}
            - {{Bar(9, "nine", "Nine", lat: "1")}}
      """;

    var keys = Merge(("base", @base)).Tables["foo.bar"].Keys.Select(key => key[0]);

    Assert.Equal([new SeedValue.Integer(9), new SeedValue.Integer(10)], keys);
  }

  [Fact]
  public void Merge_CompositeKey_IdentifiesTheRowByAllItsColumns() {
    const string Base = """
      description: Base
      tables:
        foo.link:
          insert:
            - { bar_id: 1, tag_id: 1, flags: ~, tags: ~ }
            - { bar_id: 1, tag_id: 2, flags: ~, tags: ~ }
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.link:
          update:
            - { bar_id: 1, tag_id: 1, flags: [5] }
      """;

    var rows = Merge(("base", Base), ("child", Child)).Tables["foo.link"].Values.ToList();

    Assert.Equal(2, rows.Count);
    Assert.NotNull(rows[0]["flags"]);
    Assert.Null(rows[1]["flags"]);
  }

  [Fact]
  public void Merge_ProblemsAreReportedTogether() {
    var @base = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "one", "First")}}
      """;
    var child = $$"""
      description: Child
      extends: base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "uno", "Again")}}
          update:
            - { bar_id: 8, name: Nobody }
          delete:
            - { bar_id: 7 }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => Merge(("base", @base), ("child", child)));

    Assert.Equal(
      """
      The layers have 3 problems:
      - child: foo.bar deletes the row with key 7, which no layer before it gives.
      - child: foo.bar inserts the row with key 1, which a layer before it (or this one) already gives.
      - child: foo.bar updates the row with key 8, which no layer before it gives.
      """,
      exception.Message.Replace("\r", "", StringComparison.Ordinal));
  }

  [Fact]
  public void Merge_SameKeyInsertedTwiceInOneLayer_IsRefused() {
    var yaml = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "one", "First")}}
            - {{Bar(1, "uno", "Again")}}
      """;

    var exception = Assert.Throws<SeedFormatException>(() => Merge(("base", yaml)));

    Assert.Equal("base: foo.bar inserts the row with key 1, which a layer before it (or this one) already gives.", exception.Message);
  }

  [Fact]
  public void Merge_UpdateOfARowNoLayerGives_IsRefused() {
    const string Yaml = """
      description: Base
      tables:
        foo.bar:
          update:
            - { bar_id: 1, name: Nobody }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => Merge(("base", Yaml)));

    Assert.Equal("base: foo.bar updates the row with key 1, which no layer before it gives.", exception.Message);
  }

  [Fact]
  public void Merge_AChildsUpdate_LeavesTheParentsOwnRowAlone() {
    var @base = $$"""
      description: Base
      tables:
        foo.bar:
          insert:
            - {{Bar(1, "one", "First")}}
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          update:
            - { bar_id: 1, name: Renamed }
      """;
    var parent = LayerParser.Parse("base", @base, _Schema);

    _ = LayerMerger.Merge([parent, LayerParser.Parse("child", Child, _Schema)], _Schema);

    Assert.Equal(new SeedValue.Text("First"), Assert.Single(parent.Tables[0].Inserts)["name"]);
  }

  private static SeedState Merge(params (string Name, string Yaml)[] layers) {
    var chain = new List<SeedLayer>(layers.Length);
    foreach (var (name, yaml) in layers) {
      chain.Add(LayerParser.Parse(name, yaml, _Schema));
    }
    return LayerMerger.Merge(chain, _Schema);
  }
}
