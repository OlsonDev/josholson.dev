using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Seeding;

// Each test works in a database of its own (a clone of the real schema template), because `setval` is not rolled back.
[Collection(TemplateCollection.Name)]
public sealed class SequencePositionerTests : IAsyncLifetime {
  private static readonly Instant _Anchor = Instant.FromUtc(2026, 1, 1, 12, 0);
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private ScratchDatabase _database = null!;
  private NpgsqlConnection _connection = null!;
  private SeedSchema _schema = null!;
  private LayerTree _tree = null!;

  public async ValueTask InitializeAsync() {
    _database = await ScratchDatabase.CreateAsync(_cancellationToken);
    _connection = await _database.OpenAsync(_cancellationToken);
    _schema = await ScratchDatabase.LoadSchemaAsync(_cancellationToken);
    _tree = new([
      LayerParser.Parse(
        "foo",
        """
        description: Root
        tables:
          tag.tag:
            insert:
              - { tag_id: 1, name: Corge, slug: corge, description: ~, color: ~ }
              - { tag_id: 2, name: Grault, slug: grault, description: ~, color: ~ }
        """,
        _schema),
      LayerParser.Parse(
        "bar",
        """
        description: Child
        extends: foo
        tables:
          tag.tag:
            insert:
              - { tag_id: 1001, name: Garply, slug: garply, description: ~, color: ~ }
              - { tag_id: 1002, name: Waldo, slug: waldo, description: ~, color: ~ }
        """,
        _schema),
    ]);
    await using var transaction = await _connection.BeginTransactionAsync(_cancellationToken);
    _ = await SeedApplier.ApplyAsync(transaction, _tree, _schema, "bar", _Anchor, _cancellationToken);
    await transaction.CommitAsync(_cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    await _connection.DisposeAsync();
    await _database.DisposeAsync();
  }

  private async Task<int> NextTagIdAsync()
    => await _connection.QuerySingleAsync<int>(
      "INSERT INTO tag.tag (name, slug) VALUES ('Plugh', 'plugh') RETURNING tag_id;",
      _cancellationToken);

  // Every identity column in a table that is seed data, which is how many sequences `ChainAsync` moves.
  private int IdentityColumns()
    => _schema.InsertOrder().Where(table => !table.Table.Ignored).Sum(table => table.Table.Columns.Count(
      column => column.GeneratedAs is GeneratedAs.IdentityAlways or GeneratedAs.IdentityByDefault));

  [Fact]
  public async Task ChainAsync_LayerAtDepthOne_NextGeneratedIdStartsTheDepthTwoBlock() {
    // Depth 2 is 2001 to 3000.
    var moved = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    Assert.Equal(IdentityColumns(), moved);
    Assert.Equal(2001, await NextTagIdAsync());
  }

  [Fact]
  public async Task ChainAsync_ChainWithIdsPastTheBlock_FollowsTheLargestId() {
    await _connection.ExecuteAsync(
      "INSERT INTO tag.tag (tag_id, name, slug) OVERRIDING SYSTEM VALUE VALUES (2500, 'Thud', 'thud');",
      _cancellationToken);
    var tree = new LayerTree([
      _tree.Get("foo"),
      LayerParser.Parse(
        "bar",
        """
        description: Child
        extends: foo
        tables:
          tag.tag:
            insert:
              - { tag_id: 2500, name: Thud, slug: thud, description: ~, color: ~ }
        """,
        _schema),
    ]);

    _ = await SequencePositioner.ChainAsync(_connection, tree, _schema, "bar", _cancellationToken);

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
    _ = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    var moved = await SequencePositioner.RootAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    Assert.Equal(0, moved);
    Assert.Equal(2001, await NextTagIdAsync());
  }

  [Fact]
  public async Task ChainAsync_TableWithNoRows_StartsItsSequenceAtTheNextBlockToo() {
    _ = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    var last = await _connection.QuerySingleAsync<long?>(
      "SELECT pg_sequence_last_value(pg_get_serial_sequence('tag.alias', 'alias_id')::regclass);",
      _cancellationToken);
    Assert.Equal(2000, last);
  }

  [Fact]
  public async Task ChainAsync_IgnoredTable_KeepsItsSequenceWhereItWas() {
    _ = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", _cancellationToken);

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
