using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Data;
using NodaTime;
using Xunit;

namespace JoshOlson.Database.Baking;

public sealed class SeedHydratorTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public void Anchor_ClockWithNanoseconds_IsCutToTheMicrosecond() {
    var now = Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(123_456_789);

    var anchor = SeedHydrator.Anchor(new FixedClock(now));

    Assert.Equal(Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(123_456_000), anchor);
  }

  // One database, hydrated once, is also what `SeedStateReader` is checked against: what it reads back is the chain
  // as `LayerMerger` merges it, with the anchor and the layer the hydration recorded.
  [Fact]
  public async Task HydrateAsync_ChildLayer_PutsInTheChainsRowsRecordsTheLayerAndAnchorAndMovesTheSequences() {
    var schema = SeedTestServer.Schema;
    var tree = FixtureSeeds.Tree;
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    var anchor = Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(678_000);

    var rows = await SeedHydrator.HydrateAsync(
      database.Name,
      tree,
      schema,
      FixtureSeeds.Child,
      editing: false,
      anchor,
      _cancellationToken);

    await using var connection = await database.OpenAsync(_cancellationToken);
    var stored = await connection.QuerySingleAsync<DateTime>(
      "SELECT anchor_utc FROM seed.hydration;",
      _cancellationToken);
    var recorded = await connection.QuerySingleAsync<string>(
      "SELECT layer || ' ' || editing::text FROM seed.hydration;",
      _cancellationToken);
    var read = await SeedStateReader.ReadAsync(connection, schema, _cancellationToken);
    var next = await connection.QuerySingleAsync<int>(FixtureSeeds.InsertTagReturningId("Plugh"), _cancellationToken);
    Assert.Equal($"{FixtureSeeds.Child} false", recorded);
    Assert.Equal(FixtureSeeds.ChildRows, rows);
    Assert.Equal(anchor, Instant.FromDateTimeUtc(stored));
    Assert.Equal(anchor, read.Anchor);
    Assert.Equal(FixtureSeeds.Child, read.Layer);
    // The database holds the layers' rows and the reference data its migrations made, so the layers' are a part of it.
    foreach (var (table, merged) in LayerMerger.Merge(tree.ChainTo(FixtureSeeds.Child), schema).Tables) {
      foreach (var (key, row) in merged) {
        foreach (var (column, value) in row) {
          Assert.True(
            SeedValues.Same(value, read.Rows.Tables[table][key][column]),
            $"{table} {string.Join(",", key.Select(part => part.ToString()))} {column}");
        }
      }
    }
    // The child's rows end at 1002, but a database built from it hands out IDs from the depth-2 block.
    Assert.Equal(2001, next);
  }

  // A layer that merges but that Postgres refuses (its tag has the slug of one of the root's), beside one that builds.
  [Fact]
  public async Task BuildProblemsAsync_LayerPostgresRefuses_NamesItInTheCallersWordsAndDropsEveryClone() {
    var schema = SeedTestServer.Schema;
    var databases = SeedTestServer.Databases;
    var refused = $"""
      description: A tag with a slug the root has
      extends: {FixtureSeeds.Root}
      tables:
        tag.tag:
          insert:
            - {FixtureSeeds.TagRow(1001, "Baz")}

      """;
    var tree = new LayerTree([
      FixtureSeeds.Tree.Get(FixtureSeeds.Root),
      LayerParser.Parse("foo", refused, schema),
    ]);
    var purpose = $"buildtest{Guid.NewGuid():N}"[..17];
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    var (template, _) = await SeedTestServer.RealTemplate.EnsureAsync(owner, _cancellationToken);

    var problems = await SeedHydrator.BuildProblemsAsync(
      databases,
      owner,
      template,
      purpose,
      tree,
      schema,
      [FixtureSeeds.Root, "foo"],
      Instant.FromUtc(2026, 1, 2, 3, 4, 5),
      (name, error) => $"{name} is refused: {error}",
      _cancellationToken);

    var problem = Assert.Single(problems);
    Assert.StartsWith("foo is refused: Inserting into tag.tag failed", problem, StringComparison.Ordinal);
    Assert.Empty(await databases.ListAsync(owner, $"{SeedDatabaseNames.Prefix}{purpose}_", _cancellationToken));
  }

  private sealed class FixedClock : IClock {
    private readonly Instant _now;

    public FixedClock(Instant now)
      => _now = now;

    public Instant GetCurrentInstant()
      => _now;
  }
}
