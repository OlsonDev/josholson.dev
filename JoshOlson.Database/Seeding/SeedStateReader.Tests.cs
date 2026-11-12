using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using NodaTime;
using Xunit;

namespace JoshOlson.Database.Seeding;

// Each test works in a database of its own (a clone of the real schema template).
[Collection(TemplateCollection.Name)]
public sealed class SeedStateReaderTests : IAsyncLifetime {
  private const string _KindSql = """
    CREATE SCHEMA foo;
    CREATE TABLE foo.kind (
      id         bigint PRIMARY KEY,
      flag       boolean,
      blob       bytea,
      doc        jsonb,
      address    inet,
      note       text,
      amount     numeric(9, 2),
      at_utc     timestamptz,
      seen_utc   timestamptz,
      nums       integer[],
      docs       jsonb[],
      times      timestamptz[],
      hosts      inet[],
      len        integer GENERATED ALWAYS AS (length(note)) STORED
    );
    INSERT INTO seed.hydration (anchor_utc) VALUES ('2026-01-01 12:00:00+00');
    INSERT INTO foo.kind (id, flag, blob, doc, address, note, amount, at_utc, seen_utc, nums, docs, times, hosts)
    VALUES (
      1, true, '\x00FF', '{"foo": 1, "bar": [2.50, null]}', '10.0.0.1/32', 'hello', 1.50,
      '2026-01-01 08:47:59.999999+00', '2026-01-02 03:04:05.123456+00', '{1,NULL,3}',
      ARRAY['{"b": 1, "a": 2}'::jsonb, NULL], ARRAY['2026-01-01 12:00:01+00'::timestamptz],
      ARRAY['10.0.0.0/8'::inet, '::1']
    );
    INSERT INTO foo.kind (id) VALUES (2);
    """;

  private static readonly Instant _Anchor = Instant.FromUtc(2026, 1, 1, 12, 0).PlusNanoseconds(123_000);
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private ScratchDatabase _database = null!;

  public async ValueTask InitializeAsync()
    => _database = await ScratchDatabase.CreateAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _database.DisposeAsync();

  // True when both hold the same value, an array item by item (a record's equality compares a list by reference).
  private static bool Same(SeedValue? left, SeedValue? right)
    => (left, right) switch {
      (SeedValue.Array a, SeedValue.Array b) => a.Items.Count == b.Items.Count && a.Items.Zip(b.Items).All(pair => Same(pair.First, pair.Second)),
      _ => Equals(left, right),
    };

  // The lock describing `foo.kind`, one column of every seed type, plain and as an array, and one generated from an
  // expression.
  private static SeedSchema KindSchema() {
    var kind = SchemaLockSample.NewTable("foo.kind") with {
      Columns = [
        SchemaLockSample.Column("id", "bigint"),
        SchemaLockSample.Column("flag", "boolean", nullable: true),
        SchemaLockSample.Column("blob", "bytea", nullable: true),
        SchemaLockSample.Column("doc", "jsonb", nullable: true),
        SchemaLockSample.Column("address", "inet", nullable: true),
        SchemaLockSample.Column("note", "text", nullable: true),
        SchemaLockSample.Column("amount", "decimal(9, 2)", nullable: true),
        SchemaLockSample.Column("at_utc", "timestamptz", nullable: true, policy: TimePolicy.Relative),
        SchemaLockSample.Column("seen_utc", "timestamptz", nullable: true, policy: TimePolicy.Absolute),
        SchemaLockSample.Column("nums", "integer[]", nullable: true),
        SchemaLockSample.Column("docs", "jsonb[]", nullable: true),
        SchemaLockSample.Column("times", "timestamptz[]", nullable: true, policy: TimePolicy.Relative),
        SchemaLockSample.Column("hosts", "inet[]", nullable: true),
        SchemaLockSample.Column("len", "integer", nullable: true, generatedAs: GeneratedAs.ExpressionStored),
      ],
    };
    return new(SchemaLockSample.Create() with { Tables = [kind] });
  }

  [Fact]
  public async Task ReadAsync_HydratedLayers_GivesBackTheMergedStateAndTheAnchor() {
    var schema = await ScratchDatabase.LoadSchemaAsync(_cancellationToken);
    var tree = await LayerLoader.LoadAsync(SeedPaths.ForRepository().Seeds, schema, _cancellationToken);
    _ = await SeedHydrator.HydrateAsync(_database.Name, tree, schema, SeedLayers.MoreTags, editing: false, _Anchor, _cancellationToken);
    var merged = LayerMerger.Merge(tree.ChainTo(SeedLayers.MoreTags), schema);
    await using var connection = await _database.OpenAsync(_cancellationToken);

    var read = await SeedStateReader.ReadAsync(connection, schema, _cancellationToken);

    // The database holds the layers' rows and the reference data its migrations made, so the layers' are a part of it.
    Assert.Equal(_Anchor, read.Anchor);
    Assert.Equal(SeedLayers.MoreTags, read.Layer);
    foreach (var (table, rows) in merged.Tables) {
      foreach (var (key, row) in rows) {
        foreach (var (column, value) in row) {
          Assert.True(Same(value, read.Rows.Tables[table][key][column]), $"{table} {string.Join(",", key.Select(part => part.ToString()))} {column}");
        }
      }
    }
  }

  [Fact]
  public async Task ReadAsync_FreshTemplateClone_ReadsEveryRowOfEveryTable() {
    var schema = await ScratchDatabase.LoadSchemaAsync(_cancellationToken);
    await using var connection = await _database.OpenAsync(_cancellationToken);
    await connection.ExecuteAsync("INSERT INTO seed.hydration (anchor_utc) VALUES ('2026-01-01 12:00:00+00');", _cancellationToken);

    var read = await SeedStateReader.ReadAsync(connection, schema, _cancellationToken);

    // A fresh database is not empty: the repeatable migrations put reference data in it, which no layer has. A table that is
    // `@seed ignore` is not read at all.
    var seedTables = schema.InsertOrder().Where(table => !table.Table.Ignored).ToList();
    Assert.Equal(seedTables.Select(table => table.Table.Name).Order(StringComparer.Ordinal), read.Rows.Tables.Keys.Order(StringComparer.Ordinal));
    Assert.DoesNotContain("auth.session", read.Rows.Tables.Keys);
    foreach (var table in seedTables) {
      var count = await connection.QuerySingleAsync<long>($"SELECT count(*) FROM {table.Table.Name};", _cancellationToken);
      Assert.Equal(count, read.Rows.Tables[table.Table.Name].Count);
    }
    Assert.True(read.Rows.RowCount > 0);
  }

  [Fact]
  public async Task ReadAsync_NoHydrationRow_IsRefused() {
    var schema = await ScratchDatabase.LoadSchemaAsync(_cancellationToken);
    await using var connection = await _database.OpenAsync(_cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => SeedStateReader.ReadAsync(connection, schema, _cancellationToken));

    Assert.Contains("not built by the seed tool", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ReadAsync_EveryKindOfValue_IsReadAsTheLayerReaderWouldReadIt() {
    await using var connection = await _database.OpenAsync(_cancellationToken);
    await connection.ExecuteAsync(_KindSql, _cancellationToken);

    var read = await SeedStateReader.ReadAsync(connection, KindSchema(), _cancellationToken);

    var first = read.Rows.Tables["foo.kind"][[new SeedValue.Integer(1)]];
    Assert.Equal(new SeedValue.Integer(1), first["id"]);
    Assert.Equal(new SeedValue.Boolean(true), first["flag"]);
    Assert.Equal(new SeedValue.Bytes("00ff"), first["blob"]);
    Assert.Equal(new SeedValue.Json("""{"bar":[2.50,null],"foo":1}"""), first["doc"]);
    Assert.Equal(new SeedValue.Inet("10.0.0.1"), first["address"]);
    Assert.Equal(new SeedValue.Text("hello"), first["note"]);
    Assert.Equal(new SeedValue.Decimal(1.50m), first["amount"]);
    Assert.Equal(new SeedValue.RelativeTime(-11_520_000_001), first["at_utc"]);
    Assert.Equal(new SeedValue.AbsoluteTime(Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(123_456_000)), first["seen_utc"]);
    Assert.True(Same(new SeedValue.Array([new SeedValue.Integer(1), null, new SeedValue.Integer(3)]), first["nums"]));
    Assert.True(Same(new SeedValue.Array([new SeedValue.Json("""{"a":2,"b":1}"""), null]), first["docs"]));
    Assert.True(Same(new SeedValue.Array([new SeedValue.RelativeTime(1_000_000)]), first["times"]));
    Assert.True(Same(new SeedValue.Array([new SeedValue.Inet("10.0.0.0/8"), new SeedValue.Inet("::1")]), first["hosts"]));
    Assert.DoesNotContain("len", first.Keys);
  }

  [Fact]
  public async Task ReadAsync_RowOfNulls_HasANullForEveryColumnButTheKey() {
    await using var connection = await _database.OpenAsync(_cancellationToken);
    await connection.ExecuteAsync(_KindSql, _cancellationToken);

    var read = await SeedStateReader.ReadAsync(connection, KindSchema(), _cancellationToken);

    var second = read.Rows.Tables["foo.kind"][[new SeedValue.Integer(2)]];
    Assert.Equal(new SeedValue.Integer(2), second["id"]);
    Assert.All(second.Where(cell => cell.Key is not "id"), cell => Assert.Null(cell.Value));
    Assert.Equal(_Anchor.Minus(Duration.FromNanoseconds(123_000)), read.Anchor);
  }
}
