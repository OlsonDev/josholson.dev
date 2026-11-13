using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Seeding;

// Each test works in a database of its own (a clone of the real schema template), because `setval` is not rolled back.
public sealed class SequencePositionerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private ScratchDatabase _database = null!;
  private NpgsqlConnection _connection = null!;
  private SeedSchema _schema = null!;
  private LayerTree _tree = null!;

  public async ValueTask InitializeAsync() {
    _database = await ScratchDatabase.CreateAsync(_cancellationToken);
    _connection = await _database.OpenAsync(_cancellationToken);
    _schema = SeedTestServer.Schema;
    _tree = new([
      LayerParser.Parse(
        "foo",
        $$"""
        description: Root
        tables:
          tag.tag:
            insert:
              - {{FixtureSeeds.TagRow(1, "Corge")}}
              - {{FixtureSeeds.TagRow(2, "Grault")}}
        """,
        _schema),
      LayerParser.Parse(
        "bar",
        $$"""
        description: Child
        extends: foo
        tables:
          tag.tag:
            insert:
              - {{FixtureSeeds.TagRow(1001, "Garply")}}
              - {{FixtureSeeds.TagRow(1002, "Waldo")}}
        """,
        _schema),
    ]);
    await using var transaction = await _connection.BeginTransactionAsync(_cancellationToken);
    _ = await SeedApplier.ApplyAsync(transaction, _tree, _schema, "bar", SampleLayers.Anchor, _cancellationToken);
    await transaction.CommitAsync(_cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    await _connection.DisposeAsync();
    await _database.DisposeAsync();
  }

  private async Task<int> NextTagIdAsync()
    => await _connection.QuerySingleAsync<int>(
      FixtureSeeds.InsertTagReturningId("Plugh"),
      _cancellationToken);

  // Every identity column in a table that is seed data, which is how many sequences `ChainAsync` moves.
  private int IdentityColumns()
    => _schema.InsertOrder().Where(table => !table.Table.Ignored).Sum(table => table.Table.Columns.Count(
      column => column.GeneratedAs is GeneratedAs.IdentityAlways or GeneratedAs.IdentityByDefault));

  [Fact]
  public async Task ChainAsync_LayerAtDepthOne_NextGeneratedIdStartsTheDepthTwoBlock() {
    // Depth 2 is 2001 to 3000.
    var moved = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", editing: false, _cancellationToken);

    Assert.Equal(IdentityColumns(), moved);
    Assert.Equal(2001, await NextTagIdAsync());
  }

  [Fact]
  public async Task ChainAsync_Editing_NextGeneratedIdStartsTheLayersOwnBlock() {
    // Depth 1 is 1001 to 2000.
    _ = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", editing: true, _cancellationToken);

    // The layer's own rows end at 1002, so a row made while editing it goes right after them, in its block.
    Assert.Equal(1003, await NextTagIdAsync());
  }

  [Fact]
  public async Task ChainAsync_ChainWithIdsPastTheBlock_FollowsTheLargestId() {
    await _connection.ExecuteAsync(
      FixtureSeeds.InsertTagWithId(2500, "Thud"),
      _cancellationToken);
    var tree = new LayerTree([
      _tree.Get("foo"),
      LayerParser.Parse(
        "bar",
        $$"""
        description: Child
        extends: foo
        tables:
          tag.tag:
            insert:
              - {{FixtureSeeds.TagRow(2500, "Thud")}}
        """,
        _schema),
    ]);

    _ = await SequencePositioner.ChainAsync(_connection, tree, _schema, "bar", editing: false, _cancellationToken);

    Assert.Equal(2501, await NextTagIdAsync());
  }

  [Fact]
  public async Task RootAsync_ExplicitIds_NextGeneratedIdFollowsTheRootAlone() {
    var moved = await SequencePositioner.RootAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    Assert.Equal(1, moved);
    Assert.Equal(3, await NextTagIdAsync());
  }

  [Fact]
  public async Task ChainAsync_SequenceAlreadyPastTheIds_LeavesItWhereItIs() {
    _ = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", editing: false, _cancellationToken);

    var moved = await SequencePositioner.RootAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    Assert.Equal(0, moved);
    Assert.Equal(2001, await NextTagIdAsync());
  }

  [Fact]
  public async Task ChainAsync_TableWithNoRows_StartsItsSequenceAtTheNextBlockToo() {
    _ = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", editing: false, _cancellationToken);

    var last = await _connection.QuerySingleAsync<long?>(
      "SELECT pg_sequence_last_value(pg_get_serial_sequence('tag.alias', 'alias_id')::regclass);",
      _cancellationToken);
    Assert.Equal(2000, last);
  }

  [Fact]
  public async Task ChainAsync_IgnoredTable_KeepsItsSequenceWhereItWas() {
    _ = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", editing: false, _cancellationToken);

    var untouched = await _connection.QuerySingleAsync<long?>(
      "SELECT pg_sequence_last_value(pg_get_serial_sequence('auth.session', 'session_id')::regclass);",
      _cancellationToken);
    Assert.True(_schema.Find("auth.session")!.Table.Ignored);
    Assert.Null(untouched);
  }

  [Fact]
  public async Task RootAsync_TableWithNoRows_LeavesItsSequenceAlone() {
    _ = await SequencePositioner.RootAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    var untouched = await _connection.QuerySingleAsync<long?>(
      "SELECT pg_sequence_last_value(pg_get_serial_sequence('tag.alias', 'alias_id')::regclass);",
      _cancellationToken);
    Assert.Null(untouched);
  }
}
