using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Data;
using JoshOlson.Shared.Extensions;
using Xunit;

namespace JoshOlson.Database;

// Two seed layers over the real schema that belong to the tests: a root and a child of it, in the canonical form
// `LayerWriter` writes. The real layers (`minimal`, `more-tags`) grow with the site, so a test that asserts a count, a
// color, or an ID of theirs breaks for no reason; these change only when a test needs them to. Their text is fixed, so
// their templates are built the first time and stay on the server (a name is a hash of the files).
public static class FixtureSeeds {
  public const string Root = "fixture-root";
  public const string Child = "fixture-child";

  // Three tags, one with a color.
  public static readonly string RootLayer = $"""
    description: Three tags for the seed tool's tests
    tables:
      tag.tag:
        insert:
          - {TagRow(1, "Baz", "indigo-200")}
          - {TagRow(2, "Qux")}
          - {TagRow(3, "Fred")}

    """;

  // Two more tags in the depth-1 block, and one of the root's given a color.
  public static readonly string ChildLayer = $$"""
    description: Two more tags, and a color for one of the root's
    extends: {{Root}}
    tables:
      tag.tag:
        insert:
          - {{TagRow(1001, "Xyzzy")}}
          - {{TagRow(1002, "Waldo")}}
        update:
          - { tag_id: 2, color: emerald-200 }

    """;

  private static readonly Lazy<LayerTree> _Tree = new(() => new([
    LayerParser.Parse(Root, RootLayer, SeedTestServer.Schema),
    LayerParser.Parse(Child, ChildLayer, SeedTestServer.Schema),
  ]));

  // Opened with no token of a test's, so a test that is canceled does not leave a canceled task for the others. The
  // workspace under it lives as long as the process: the tool reads the layer files again whenever it names a template.
  private static readonly Lazy<Task<SeedTool>> _Tool = new(async () => {
    var workspace = await WorkspaceAsync(CancellationToken.None);
    AppDomain.CurrentDomain.ProcessExit += (_, _) => workspace.Dispose();
    return await workspace.OpenToolAsync(CancellationToken.None);
  });

  // The two layers as the parser reads them.
  public static LayerTree Tree
    => _Tree.Value;

  // How many rows a database built from the root, or from the child, has of the layers' own (the reference data the
  // migrations made is not counted).
  public static int RootRows
    => LayerMerger.Merge(Tree.ChainTo(Root), SeedTestServer.Schema).RowCount;

  public static int ChildRows
    => LayerMerger.Merge(Tree.ChainTo(Child), SeedTestServer.Schema).RowCount;

  // The tool over a workspace with the two layers, opened once per test run. `SeedTool` only reads what it was opened
  // with (its fields are read-only, and so are the templates and the tree it holds), and `WithDevelopment` returns
  // another tool, so tests that run side by side can share it.
  public static Task<SeedTool> Tool
    => _Tool.Value;

  // A workspace of the caller's own: the real migrations and lock, so its schema template is the real one, and the two
  // fixture layers in place of the real ones. The caller disposes it.
  public static async Task<SeedWorkspace> WorkspaceAsync(CancellationToken cancellationToken) {
    var workspace = SeedWorkspace.CopyOfRepository("fixture-seeds", withLayers: false);
    try {
      await workspace.WriteLayerAsync(Root, RootLayer, cancellationToken);
      await workspace.WriteLayerAsync(Child, ChildLayer, cancellationToken);
    } catch {
      workspace.Dispose();
      throw;
    }
    return workspace;
  }

  // A `tag.tag` row as a layer writes it, which names every column. The slug is the name in lower case.
  public static string TagRow(int id, string name, string color = "~")
    => $"{{ tag_id: {id.ToInvariantString()}, name: {name}, slug: {Slug(name)}, description: ~, color: {color} }}";

  // SQL that makes a tag the way the app would, its ID from the sequence. The name is a test's own literal, never data
  // from outside, which is why it may be written into the text.
  public static string InsertTag(string name)
    => $"INSERT INTO tag.tag (name, slug) VALUES ('{name}', '{Slug(name)}');";

  // The same, returning the ID the sequence gave it.
  public static string InsertTagReturningId(string name)
    => $"INSERT INTO tag.tag (name, slug) VALUES ('{name}', '{Slug(name)}') RETURNING tag_id;";

  // The same with an ID the test chose.
  public static string InsertTagWithId(int id, string name)
    => "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE "
      + $"VALUES ({id.ToInvariantString()}, '{name}', '{Slug(name)}');";

  private static string Slug(string name)
    => name.ToLowerInvariant().Replace(' ', '-');
}

public sealed class FixtureSeedsTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task Tool_FixtureLayers_AreCanonicalBakeAndGiveADatabaseWithTheChainsRows() {
    var tool = await FixtureSeeds.Tool;
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    var id = Guid.NewGuid().ToString("N");
    try {
      var baked = await tool.BakeAsync(owner, _cancellationToken);
      var database = await tool.CreateTestDatabaseAsync(owner, id, FixtureSeeds.Child, _cancellationToken);

      await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, _cancellationToken);
      var tags = await connection.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken);
      var next = await connection.QuerySingleAsync<int>(FixtureSeeds.InsertTagReturningId("Plugh"), _cancellationToken);
      Assert.Equal(["(schema)", FixtureSeeds.Root, FixtureSeeds.Child], baked.Select(template => template.Layer));
      Assert.Equal(await SeedTestServer.RealTemplate.NameAsync(_cancellationToken), baked[0].Name);
      Assert.Equal(FixtureSeeds.RootLayer, LayerWriter.Write(tool.Tree.Get(FixtureSeeds.Root), SeedTestServer.Schema));
      var child = LayerWriter.Write(tool.Tree.Get(FixtureSeeds.Child), SeedTestServer.Schema);
      Assert.Equal(FixtureSeeds.ChildLayer, child);
      Assert.Equal(3, FixtureSeeds.RootRows);
      Assert.Equal(5, FixtureSeeds.ChildRows);
      Assert.Equal(FixtureSeeds.ChildRows, tags);
      // The child is at depth 1, so a database built from it hands out IDs from the depth-2 block.
      Assert.Equal(2001, next);
    } finally {
      await SeedTestServer.Databases.DropAsync(owner, SeedDatabaseNames.Test(id), CancellationToken.None);
    }
  }
}
