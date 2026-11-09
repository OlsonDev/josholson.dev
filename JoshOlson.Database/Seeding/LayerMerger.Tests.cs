using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using Xunit;

namespace JoshOlson.Database.Seeding;

public class LayerMergerTests {
  private static readonly SeedSchema _Schema = new(SchemaLockSample.Create());

  [Fact]
  public void Merge_ChildChangesOnlyTheColumnsItNames() {
    const string Base = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1.5 }
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, name: Renamed }
            - { bar_id: 2, slug: two, name: Second, lat: 2.5 }
      """;

    var state = Merge(("base", Base), ("child", Child));

    var rows = state.Tables["foo.bar"].Values.ToList();
    Assert.Equal(2, rows.Count);
    Assert.Equal(new SeedValue.Text("Renamed"), rows[0]["name"]);
    Assert.Equal(new SeedValue.Text("one"), rows[0]["slug"]);
    Assert.Equal(new SeedValue.Text("Second"), rows[1]["name"]);
    Assert.Equal(2, state.RowCount);
  }

  [Fact]
  public void Merge_ExplicitNull_ReplacesTheValue() {
    const string Base = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1.5, note: hello }
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, note: ~ }
      """;

    var state = Merge(("base", Base), ("child", Child));

    Assert.Null(state.Tables["foo.bar"].Values.Single()["note"]);
  }

  [Fact]
  public void Merge_DeleteRemovesTheRow() {
    const string Base = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1.5 }
            - { bar_id: 2, slug: two, name: Second, lat: 2.5 }
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          delete:
            - { bar_id: 1 }
      """;

    var state = Merge(("base", Base), ("child", Child));

    Assert.Equal(new SeedValue.Text("two"), state.Tables["foo.bar"].Values.Single()["slug"]);
  }

  [Fact]
  public void Merge_InOneLayer_DeletesApplyBeforeUpserts() {
    const string Base = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First, lat: 1.5, note: old }
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          delete:
            - { bar_id: 1 }
          upsert:
            - { bar_id: 1, slug: uno, name: Again, lat: 3.5 }
      """;

    var row = Merge(("base", Base), ("child", Child)).Tables["foo.bar"].Values.Single();

    Assert.Equal(new SeedValue.Text("uno"), row["slug"]);
    Assert.False(row.ContainsKey("note"));
  }

  [Fact]
  public void Merge_RowsComeOutInKeyOrder() {
    const string Base = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 10, slug: ten, name: Ten, lat: 1 }
            - { bar_id: 9, slug: nine, name: Nine, lat: 1 }
      """;

    var keys = Merge(("base", Base)).Tables["foo.bar"].Keys.Select(key => key[0]);

    Assert.Equal([new SeedValue.Integer(9), new SeedValue.Integer(10)], keys);
  }

  [Fact]
  public void Merge_CompositeKey_IdentifiesTheRowByAllItsColumns() {
    const string Base = """
      description: Base
      tables:
        foo.link:
          upsert:
            - { bar_id: 1, tag_id: 1 }
            - { bar_id: 1, tag_id: 2 }
            - { bar_id: 1, tag_id: 1, flags: [5] }
      """;

    var rows = Merge(("base", Base)).Tables["foo.link"].Values.ToList();

    Assert.Equal(2, rows.Count);
    Assert.True(rows[0].ContainsKey("flags"));
  }

  [Fact]
  public void Merge_ProblemsAreReportedTogether() {
    const string Base = """
      description: Base
      tables:
        foo.bar:
          upsert:
            - { bar_id: 1, slug: one, name: First }
      """;
    const string Child = """
      description: Child
      extends: base
      tables:
        foo.bar:
          delete:
            - { bar_id: 7 }
      """;

    var exception = Assert.Throws<SeedFormatException>(() => Merge(("base", Base), ("child", Child)));

    Assert.Equal(
      """
      The layers have 2 problems:
      - child: foo.bar deletes the row with key 7, which no layer before it gives.
      - foo.bar row 1: lat is NOT NULL and has no default, and no layer gives it a value.
      """,
      exception.Message.Replace("\r", "", StringComparison.Ordinal));
  }

  [Fact]
  public void Merge_ColumnsWithDefaultsAndIdentitiesAreNotRequired() {
    const string Base = """
      description: Base
      tables:
        foo.tag:
          upsert:
            - { tag_id: 1 }
      """;

    Assert.Equal(1, Merge(("base", Base)).RowCount);
  }

  private static SeedState Merge(params (string Name, string Yaml)[] layers) {
    var chain = new List<SeedLayer>(layers.Length);
    foreach (var (name, yaml) in layers) {
      chain.Add(LayerParser.Parse(name, yaml, _Schema));
    }
    return LayerMerger.Merge(chain, _Schema);
  }
}
