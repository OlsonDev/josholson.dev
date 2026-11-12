using JoshOlson.Database.Layers;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class StateDifferTests {
  private static SeedLayer Base()
    => SampleLayers.Layer("base", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "One")}}
            - {{SampleLayers.Bar(2, "Two", note: "hello")}}
            - {{SampleLayers.Bar(3, "Three")}}
        foo.link:
          insert:
            - {{SampleLayers.Link(1, 1, flags: "[1]")}}
            - {{SampleLayers.Link(1, 2)}}
      """);

  // What `child` changes: the difference between the base alone and the base with `child` on it.
  private static List<TableChange> DiffOf(SeedLayer child)
    => StateDiffer.Diff(SampleLayers.Merge(Base()), SampleLayers.Merge(Base(), child), SampleLayers.Schema);

  [Fact]
  public void Diff_NewRow_IsAnInsertOfEveryColumn() {
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(4, "Four")}}
      """);

    var change = Assert.Single(DiffOf(child));

    Assert.Equal("foo.bar", change.Table);
    Assert.Empty(change.Updates);
    Assert.Empty(change.Deletes);
    var row = Assert.Single(change.Inserts);
    Assert.Equal(["bar_id", "created_utc", "lat", "name", "note", "published_utc", "slug"], row.Keys.Order(StringComparer.Ordinal));
    Assert.Equal(new SeedValue.Integer(4), row["bar_id"]);
    Assert.Equal(new SeedValue.Text("Four"), row["name"]);
  }

  [Fact]
  public void Diff_ChangedRow_IsAnUpdateOfTheKeyAndOnlyTheColumnsThatChanged() {
    var child = SampleLayers.Layer("child", "base", """
        foo.bar:
          update:
            - { bar_id: 2, name: Deux, note: ~ }
      """);

    var change = Assert.Single(DiffOf(child));

    Assert.Empty(change.Inserts);
    Assert.Empty(change.Deletes);
    var update = Assert.Single(change.Updates);
    Assert.Equal(["bar_id", "name", "note"], update.Keys.Order(StringComparer.Ordinal));
    Assert.Equal(new SeedValue.Text("Deux"), update["name"]);
    Assert.Null(update["note"]);
  }

  [Fact]
  public void Diff_GoneRow_IsADeleteOfTheKey() {
    var child = SampleLayers.Layer("child", "base", """
        foo.bar:
          delete:
            - { bar_id: 3 }
      """);

    var change = Assert.Single(DiffOf(child));

    Assert.Empty(change.Inserts);
    Assert.Empty(change.Updates);
    var delete = Assert.Single(change.Deletes);
    Assert.Equal(new SeedValue.Integer(3), Assert.Single(delete).Value);
  }

  [Fact]
  public void Diff_NothingChanged_IsEmpty()
    => Assert.Empty(StateDiffer.Diff(SampleLayers.Merge(Base()), SampleLayers.Merge(Base()), SampleLayers.Schema));

  [Fact]
  public void Diff_ChangedRelativeTime_IsAnUpdateOfThatOffset() {
    var child = SampleLayers.Layer("child", "base", """
        foo.bar:
          update:
            - { bar_id: 1, created_utc: 4s }
      """);

    var update = Assert.Single(Assert.Single(DiffOf(child)).Updates);

    Assert.Equal(new SeedValue.RelativeTime(4_000_000), update["created_utc"]);
    Assert.Equal(2, update.Count);
  }

  [Fact]
  public void Diff_ArrayThatHoldsTheSameItems_IsNotAChangeButOneThatDiffersIs() {
    var same = SampleLayers.Layer("same", "base", """
        foo.link:
          update:
            - { bar_id: 1, tag_id: 1, flags: [1] }
      """);
    var different = SampleLayers.Layer("different", "base", """
        foo.link:
          update:
            - { bar_id: 1, tag_id: 1, flags: [1, 2] }
            - { bar_id: 1, tag_id: 2, flags: [5] }
      """);

    var unchanged = DiffOf(same);
    var changed = Assert.Single(DiffOf(different));

    Assert.Empty(unchanged);
    Assert.Equal(2, changed.Updates.Count);
  }

  [Fact]
  public void Diff_CompositeKey_NamesEveryKeyColumnInADelete() {
    var child = SampleLayers.Layer("child", "base", """
        foo.link:
          delete:
            - { bar_id: 1, tag_id: 2 }
      """);

    var delete = Assert.Single(Assert.Single(DiffOf(child)).Deletes);

    Assert.Equal(["bar_id", "tag_id"], delete.Keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public void Diff_TableOnlyTheChildHas_IsAllInserts() {
    var onlyBar = SampleLayers.Layer("only", extends: null, $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(1, "One")}}
      """);

    var change = Assert.Single(StateDiffer.Diff(new SeedState(), SampleLayers.Merge(onlyBar), SampleLayers.Schema));

    Assert.Single(change.Inserts);
  }

  [Fact]
  public void Diff_TableOnlyTheParentHas_IsAllDeletes() {
    var onlyLink = SampleLayers.Layer("only", extends: null, $$"""
        foo.link:
          insert:
            - {{SampleLayers.Link(1, 1)}}
      """);

    var change = Assert.Single(StateDiffer.Diff(SampleLayers.Merge(onlyLink), new SeedState(), SampleLayers.Schema));

    Assert.Single(change.Deletes);
    Assert.Empty(change.Inserts);
  }

  [Fact]
  public void Diff_TablesAreInNameOrder() {
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.link:
          delete:
            - { bar_id: 1, tag_id: 2 }
        foo.bar:
          insert:
            - {{SampleLayers.Bar(9, "Nine")}}
      """);

    var tables = DiffOf(child).Select(change => change.Table);

    Assert.Equal(["foo.bar", "foo.link"], tables);
  }

  [Fact]
  public void Diff_OfTheStateBeforeAndAfterALayer_IsThatLayerInCanonicalText() {
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(10, "Ten", created: "3d 4h")}}
            - {{SampleLayers.Bar(11, "Eleven")}}
          update:
            - { bar_id: 1, name: Uno }
            - { bar_id: 2, note: ~, created_utc: 1.5s }
          delete:
            - { bar_id: 3 }
        foo.link:
          update:
            - { bar_id: 1, tag_id: 2, flags: [7, 8] }
      """);
    var diff = new SeedLayer {
      Name = "child",
      Extends = "base",
      Description = "child",
      Tables = DiffOf(child),
    };

    Assert.Equal(LayerWriter.Write(child, SampleLayers.Schema), LayerWriter.Write(diff, SampleLayers.Schema));
  }

  [Fact]
  public void Diff_AppliedToTheParent_GivesTheChildBack() {
    var child = SampleLayers.Layer("child", "base", $$"""
        foo.bar:
          insert:
            - {{SampleLayers.Bar(10, "Ten")}}
          update:
            - { bar_id: 1, name: Uno }
          delete:
            - { bar_id: 3 }
      """);
    var parentState = SampleLayers.Merge(Base());
    var childState = SampleLayers.Merge(Base(), child);
    var diff = new SeedLayer {
      Name = "diff",
      Extends = null,
      Description = "diff",
      Tables = StateDiffer.Diff(parentState, childState, SampleLayers.Schema),
    };

    var rebuilt = SampleLayers.Merge(StateLayer.Of(parentState, "parent"), diff);

    Assert.True(SeedValues.SameState(childState, rebuilt));
  }
}
