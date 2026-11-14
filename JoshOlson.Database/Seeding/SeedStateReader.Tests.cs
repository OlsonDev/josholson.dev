using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using NodaTime;
using Xunit;

namespace JoshOlson.Database.Seeding;

// Each test works in a database of its own (a clone of the real schema template). Reading a database a layer chain was
// put into is `SeedHydratorTests`' test, which hydrates one and reads it back here.
public sealed class SeedStateReaderTests {
  // `foo.kind` as `SchemaLockSample.KindTable` describes it, with a row of values and a row of nulls. The database has
  // none of the sample's other tables, so these tests read it with `SampleLayers.SchemaOfKind`.
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

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  // The two kinds of template, each through a clone that is gone afterwards: the schema template has no anchor of its
  // own and a layer's has, with the layer's name.
  [Fact]
  public async Task ReadTemplateAsync_SchemaAndLayerTemplates_ReadsEachThroughACloneItDrops() {
    var schema = SeedTestServer.Schema;
    var databases = SeedTestServer.Databases;
    var anchor = Instant.FromUtc(2026, 1, 1, 12, 0);
    var purpose = $"readtest{Guid.NewGuid():N}"[..16];
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    var (schemaTemplate, _) = await SeedTestServer.RealTemplate.EnsureAsync(owner, _cancellationToken);
    var tool = await FixtureSeeds.Tool.WaitAsync(_cancellationToken);
    var layerTemplate = (await tool.BakeAsync(owner, _cancellationToken))
      .Single(baked => baked.Layer is FixtureSeeds.Root)
      .Name;

    var unrecorded = await Assert.ThrowsAsync<SeedFormatException>(
      () => SeedStateReader.ReadTemplateAsync(databases, owner, schemaTemplate, purpose, schema, _cancellationToken));
    var bare = await SeedStateReader.ReadTemplateAsync(
      databases,
      owner,
      schemaTemplate,
      purpose,
      schema,
      _cancellationToken,
      anchor);
    var root = await SeedStateReader.ReadTemplateAsync(
      databases,
      owner,
      layerTemplate,
      purpose,
      schema,
      _cancellationToken,
      anchor);

    Assert.Contains("not built by the seed tool", unrecorded.Message, StringComparison.Ordinal);
    Assert.Equal(anchor, bare.Anchor);
    Assert.Null(bare.Layer);
    Assert.Empty(bare.Rows.Tables["tag.tag"]);
    Assert.NotEqual(anchor, root.Anchor);
    Assert.Equal(FixtureSeeds.Root, root.Layer);
    Assert.Equal(FixtureSeeds.RootRows, root.Rows.Tables["tag.tag"].Count);
    Assert.Empty(await databases.ListAsync(owner, $"{SeedDatabaseNames.Prefix}{purpose}_", _cancellationToken));
  }

  // One clone, before and after its one `INSERT`: a database the seed tool did not build is refused, and one with the
  // row and nothing else reads as the reference data the migrations made.
  [Fact]
  public async Task ReadAsync_TemplateClone_IsRefusedUntilItHasAHydrationRowAndThenReadsEveryRowOfEveryTable() {
    var schema = SeedTestServer.Schema;
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => SeedStateReader.ReadAsync(connection, schema, _cancellationToken));
    await connection.ExecuteAsync(
      "INSERT INTO seed.hydration (anchor_utc) VALUES ('2026-01-01 12:00:00+00');",
      _cancellationToken);
    var read = await SeedStateReader.ReadAsync(connection, schema, _cancellationToken);

    Assert.Contains("not built by the seed tool", exception.Message, StringComparison.Ordinal);
    // A fresh database is not empty: the repeatable migrations put reference data in it, which no layer has. A table
    // that is `@seed ignore` is not read at all.
    var seedTables = schema.InsertOrder().Where(table => !table.Table.Ignored).ToList();
    Assert.Equal(
      seedTables.Select(table => table.Table.Name).Order(StringComparer.Ordinal),
      read.Rows.Tables.Keys.Order(StringComparer.Ordinal));
    Assert.DoesNotContain("auth.session", read.Rows.Tables.Keys);
    foreach (var table in seedTables) {
      var count = await connection.QuerySingleAsync<long>(
        $"SELECT count(*) FROM {table.Table.Name};",
        _cancellationToken);
      Assert.Equal(count, read.Rows.Tables[table.Table.Name].Count);
    }
    Assert.True(read.Rows.RowCount > 0);
  }

  [Fact]
  public async Task ReadAsync_EveryKindOfValueAndARowOfNulls_AreReadAsTheLayerReaderWouldReadThem() {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);
    await connection.ExecuteAsync(_KindSql, _cancellationToken);

    var read = await SeedStateReader.ReadAsync(connection, SampleLayers.SchemaOfKind, _cancellationToken);

    var first = read.Rows.Tables["foo.kind"][[new SeedValue.Integer(1)]];
    Assert.Equal(new SeedValue.Integer(1), first["id"]);
    Assert.Equal(new SeedValue.Boolean(true), first["flag"]);
    Assert.Equal(new SeedValue.Bytes("00ff"), first["blob"]);
    Assert.Equal(new SeedValue.Json("""{"bar":[2.50,null],"foo":1}"""), first["doc"]);
    Assert.Equal(new SeedValue.Inet("10.0.0.1"), first["address"]);
    Assert.Equal(new SeedValue.Text("hello"), first["note"]);
    Assert.Equal(new SeedValue.Decimal(1.50m), first["amount"]);
    Assert.Equal(new SeedValue.RelativeTime(-11_520_000_001), first["at_utc"]);
    Assert.Equal(
      new SeedValue.AbsoluteTime(Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(123_456_000)),
      first["seen_utc"]);
    var nums = new SeedValue.Array([new SeedValue.Integer(1), null, new SeedValue.Integer(3)]);
    var hosts = new SeedValue.Array([new SeedValue.Inet("10.0.0.0/8"), new SeedValue.Inet("::1")]);
    Assert.True(SeedValues.Same(nums, first["nums"]));
    Assert.True(SeedValues.Same(new SeedValue.Array([new SeedValue.Json("""{"a":2,"b":1}"""), null]), first["docs"]));
    Assert.True(SeedValues.Same(new SeedValue.Array([new SeedValue.RelativeTime(1_000_000)]), first["times"]));
    Assert.True(SeedValues.Same(hosts, first["hosts"]));
    Assert.DoesNotContain("len", first.Keys);
    var second = read.Rows.Tables["foo.kind"][[new SeedValue.Integer(2)]];
    Assert.Equal(new SeedValue.Integer(2), second["id"]);
    Assert.All(second.Where(cell => cell.Key is not "id"), cell => Assert.Null(cell.Value));
    Assert.Equal(Instant.FromUtc(2026, 1, 1, 12, 0), read.Anchor);
  }
}
