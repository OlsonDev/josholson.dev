using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Seeding;

public sealed class SequencePositionerTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly SeedSchema _schema = SeedTestServer.Schema;

  // Where a table's identity sequence is, read without spending a value: null until it has been moved or used.
  private async Task<long?> LastValueAsync(NpgsqlConnection connection, string table, string column)
    => await connection.QuerySingleAsync<long?>(
      "SELECT pg_sequence_last_value(pg_get_serial_sequence(@table, @column)::regclass);",
      new { table, column },
      _cancellationToken);

  private async Task<long?> LastTagIdAsync(NpgsqlConnection connection)
    => await LastValueAsync(connection, "tag.tag", "tag_id");

  // Every identity column in a table that is seed data, which is how many sequences `ChainAsync` moves.
  private int IdentityColumns()
    => _schema.InsertOrder().Where(table => !table.Table.Ignored).Sum(table => table.Table.Columns.Count(
      column => column.GeneratedAs is GeneratedAs.IdentityAlways or GeneratedAs.IdentityByDefault));

  private async Task<int> RootAsync(NpgsqlConnection connection)
    => await SequencePositioner.RootAsync(
      connection,
      FixtureSeeds.Tree,
      _schema,
      FixtureSeeds.Child,
      _cancellationToken);

  private async Task<int> ChainAsync(NpgsqlConnection connection, bool editing)
    => await SequencePositioner.ChainAsync(
      connection,
      FixtureSeeds.Tree,
      _schema,
      FixtureSeeds.Child,
      editing,
      _cancellationToken);

  // The fixture root and a child of it with a row far past the depth-1 block.
  private LayerTree ChainPastItsBlock()
    => new([
      FixtureSeeds.Tree.Get(FixtureSeeds.Root),
      LayerParser.Parse(
        "foo",
        $$"""
        description: Child
        extends: {{FixtureSeeds.Root}}
        tables:
          tag.tag:
            insert:
              - {{FixtureSeeds.TagRow(2500, "Thud")}}
        """,
        _schema),
    ]);

  // One database (a clone of the real schema template) with the two fixture layers applied carries every step:
  // `setval` is not rolled back, but a sequence only moves forward, so the steps go in the order that does. The root's
  // rows end at 3 and the child's, in the depth-1 block (1001 to 2000), at 1002.
  // 1. `RootAsync` follows the root alone, and leaves a table with no rows where it was.
  // 2. `ChainAsync` while editing the child stays in the child's own block, right after its rows.
  // 3. `ChainAsync` moves every sequence to the start of the depth-2 block, a table with no rows too, but not a table
  //    that is `@seed ignore`.
  // 4. `RootAsync` again finds the sequence past the root's IDs and leaves it.
  // 5. A chain with an ID past its block is followed to that ID, and the next generated ID comes right after it.
  [Fact]
  public async Task RootAsyncAndChainAsync_OneDatabaseStepByStep_MoveEachSequenceOnlyForwardToWhereTheNextIdBelongs() {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);
    await using (var transaction = await connection.BeginTransactionAsync(_cancellationToken)) {
      _ = await SeedApplier.ApplyAsync(
        transaction,
        FixtureSeeds.Tree,
        _schema,
        FixtureSeeds.Child,
        SampleLayers.Anchor,
        _cancellationToken);
      await transaction.CommitAsync(_cancellationToken);
    }

    Assert.Equal(1, await RootAsync(connection));
    Assert.Equal(3, await LastTagIdAsync(connection));
    Assert.Null(await LastValueAsync(connection, "tag.alias", "alias_id"));

    _ = await ChainAsync(connection, editing: true);
    Assert.Equal(1002, await LastTagIdAsync(connection));

    Assert.Equal(IdentityColumns(), await ChainAsync(connection, editing: false));
    Assert.Equal(2000, await LastTagIdAsync(connection));
    Assert.Equal(2000, await LastValueAsync(connection, "tag.alias", "alias_id"));
    Assert.True(_schema.Find("auth.session")!.Table.Ignored);
    Assert.Null(await LastValueAsync(connection, "auth.session", "session_id"));

    Assert.Equal(0, await RootAsync(connection));
    Assert.Equal(2000, await LastTagIdAsync(connection));

    await connection.ExecuteAsync(FixtureSeeds.InsertTagWithId(2500, "Thud"), _cancellationToken);
    _ = await SequencePositioner.ChainAsync(
      connection,
      ChainPastItsBlock(),
      _schema,
      "foo",
      editing: false,
      _cancellationToken);
    Assert.Equal(2500, await LastTagIdAsync(connection));
    var next = await connection.QuerySingleAsync<int>(FixtureSeeds.InsertTagReturningId("Plugh"), _cancellationToken);
    Assert.Equal(2501, next);
  }
}
