using JoshOlson.Database.Layers;
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

  [Fact]
  public async Task HydrateAsync_Layer_PutsInItsRowsTheAnchorAndMovesTheSequences() {
    var schema = SeedTestServer.Schema;
    var tree = new LayerTree([
      LayerParser.Parse(
        "foo",
        $$"""
        description: Root
        tables:
          tag.tag:
            insert:
              - {{FixtureSeeds.TagRow(7, "Corge")}}
        """,
        schema),
    ]);
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    var anchor = Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(678_000);

    var rows = await SeedHydrator.HydrateAsync(database.Name, tree, schema, "foo", editing: false, anchor, _cancellationToken);

    await using var connection = await database.OpenAsync(_cancellationToken);
    var stored = await connection.QuerySingleAsync<DateTime>("SELECT anchor_utc FROM seed.hydration;", _cancellationToken);
    var next = await connection.QuerySingleAsync<int>(
      FixtureSeeds.InsertTagReturningId("Grault"),
      _cancellationToken);
    var recorded = await connection.QuerySingleAsync<string>("SELECT layer || ' ' || editing::text FROM seed.hydration;", _cancellationToken);
    Assert.Equal("foo false", recorded);
    Assert.Equal(1, rows);
    Assert.Equal(anchor, Instant.FromDateTimeUtc(stored));
    // The root's rows end at 7, but a database built from it hands out IDs from the depth-1 block.
    Assert.Equal(1001, next);
  }

  private sealed class FixedClock : IClock {
    private readonly Instant _now;

    public FixedClock(Instant now)
      => _now = now;

    public Instant GetCurrentInstant()
      => _now;
  }
}
