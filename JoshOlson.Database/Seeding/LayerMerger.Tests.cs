using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class LayerMergerTests {
  [Fact]
  public void Merge_ChildChangesOnlyTheColumnsItNames() {
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First", slug: "one")}}
      """);
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(2, "Second")}}
          update:
            - { bar_id: 1, name: Renamed }
      """);

    var state = SampleLayers.Merge(@base, child);

    var rows = state.Tables["foo.bar"].Values.ToList();
    Assert.Equal(2, rows.Count);
    Assert.Equal(new SeedValue.Text("Renamed"), rows[0]["name"]);
    Assert.Equal(new SeedValue.Text("one"), rows[0]["slug"]);
    Assert.Equal(new SeedValue.Text("Second"), rows[1]["name"]);
    Assert.Equal(2, state.RowCount);
  }

  [Fact]
  public void Merge_ExplicitNull_ReplacesTheValue() {
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First", note: "hello")}}
      """);
    var child = SampleLayers.Layer("child", "base", """
        foo.bar:
          update:
            - { bar_id: 1, note: ~ }
      """);

    var state = SampleLayers.Merge(@base, child);

    Assert.Null(state.Tables["foo.bar"].Values.Single()["note"]);
  }

  [Fact]
  public void Merge_DeleteRemovesTheRow() {
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First", slug: "one")}}
            - {{SampleLayers.Bar(2, "Second", slug: "two")}}
      """);
    var child = SampleLayers.Layer("child", "base", """
        foo.bar:
          delete:
            - { bar_id: 1 }
      """);

    var state = SampleLayers.Merge(@base, child);

    Assert.Equal(new SeedValue.Text("two"), state.Tables["foo.bar"].Values.Single()["slug"]);
  }

  [Fact]
  public void Merge_InOneLayer_DeletesApplyBeforeInserts() {
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First", slug: "one", note: "old")}}
      """);
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "Again", slug: "uno")}}
          delete:
            - { bar_id: 1 }
      """);

    var row = SampleLayers.Merge(@base, child).Tables["foo.bar"].Values.Single();

    Assert.Equal(new SeedValue.Text("uno"), row["slug"]);
    Assert.Null(row["note"]);
  }

  [Fact]
  public void Merge_RowsComeOutInKeyOrder() {
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(10, "Ten")}}
            - {{SampleLayers.Bar(9, "Nine")}}
      """);

    var keys = SampleLayers.Merge(@base).Tables["foo.bar"].Keys.Select(key => key[0]);

    Assert.Equal([new SeedValue.Integer(9), new SeedValue.Integer(10)], keys);
  }

  [Fact]
  public void Merge_CompositeKey_IdentifiesTheRowByAllItsColumns() {
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.link:
          insert:
            - {{SampleLayers.Link(1, 1)}}
            - {{SampleLayers.Link(1, 2)}}
      """);
    var child = SampleLayers.Layer("child", "base", """
        foo.link:
          update:
            - { bar_id: 1, tag_id: 1, flags: [5] }
      """);

    var rows = SampleLayers.Merge(@base, child).Tables["foo.link"].Values.ToList();

    Assert.Equal(2, rows.Count);
    Assert.NotNull(rows[0]["flags"]);
    Assert.Null(rows[1]["flags"]);
  }

  [Fact]
  public void Merge_ProblemsAreReportedTogether() {
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First")}}
      """);
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "Again")}}
          update:
            - { bar_id: 8, name: Nobody }
          delete:
            - { bar_id: 7 }
      """);

    var exception = Assert.Throws<SeedFormatException>(() => SampleLayers.Merge(@base, child));

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
    var @base = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First")}}
            - {{SampleLayers.Bar(1, "Again")}}
      """);

    var exception = Assert.Throws<SeedFormatException>(() => SampleLayers.Merge(@base));

    Assert.Equal(
      "base: foo.bar inserts the row with key 1, which a layer before it (or this one) already gives.",
      exception.Message);
  }

  [Fact]
  public void Merge_UpdateOfARowNoLayerGives_IsRefused() {
    var @base = SampleLayers.Layer("base", extends: null, """
        foo.bar:
          update:
            - { bar_id: 1, name: Nobody }
      """);

    var exception = Assert.Throws<SeedFormatException>(() => SampleLayers.Merge(@base));

    Assert.Equal("base: foo.bar updates the row with key 1, which no layer before it gives.", exception.Message);
  }

  [Fact]
  public void Merge_AChildsUpdate_LeavesTheParentsOwnRowAlone() {
    var parent = SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "First")}}
      """);
    var child = SampleLayers.Layer("child", "base", """
        foo.bar:
          update:
            - { bar_id: 1, name: Renamed }
      """);

    _ = SampleLayers.Merge(parent, child);

    Assert.Equal(new SeedValue.Text("First"), Assert.Single(parent.Tables[0].Inserts)["name"]);
  }
}
