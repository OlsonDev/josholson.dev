using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
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
            upsert:
              - { tag_id: 1, name: Corge, slug: corge }
              - { tag_id: 2, name: Grault, slug: grault }
        """,
        _schema),
      LayerParser.Parse(
        "bar",
        """
        description: Child
        extends: foo
        tables:
          tag.tag:
            upsert:
              - { tag_id: 1000, name: Garply, slug: garply }
              - { tag_id: 1001, name: Waldo, slug: waldo }
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
      null,
      _cancellationToken);

  [Fact]
  public async Task ChainAsync_ExplicitIds_NextGeneratedIdFollowsTheWholeChain() {
    var moved = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    Assert.Equal(1, moved);
    Assert.Equal(1002, await NextTagIdAsync());
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
    Assert.Equal(1002, await NextTagIdAsync());
  }

  [Fact]
  public async Task ChainAsync_TableWithNoRows_LeavesItsSequenceAlone() {
    var moved = await SequencePositioner.ChainAsync(_connection, _tree, _schema, "bar", _cancellationToken);

    var untouched = await _connection.QuerySingleAsync<long?>(
      "SELECT pg_sequence_last_value(pg_get_serial_sequence('tag.alias', 'alias_id')::regclass);",
      null,
      _cancellationToken);
    Assert.Equal(1, moved);
    Assert.Null(untouched);
  }
}
