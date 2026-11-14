using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Clones and rebuilds from the fixture layers, with the tool every test shares; the two tests whose subject is the
// real layers bake and check those, and say nothing about what is in them. The templates are left for the next run.
// Everything else these tests make is named `seed_test_*`, `seed_tooltest_*`, or `seed_cleantest_*` and dropped at
// the end, and the dev database itself is never touched: the rebuild test points the tool at a database of its own.
public sealed class SeedToolTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ServerDatabases _databases = SeedTestServer.Databases;
  private NpgsqlConnection _owner = null!;
  private SeedTool _tool = null!;

  public async ValueTask InitializeAsync() {
    _tool = await FixtureSeeds.Tool;
    _owner = await DatabaseEnvironment.OpenOwnerAsync(_cancellationToken);
  }

  public async ValueTask DisposeAsync()
    => await _owner.DisposeAsync();

  [Fact]
  public async Task BakeAsync_RealLayers_BuildsEachTemplateOnce() {
    var tool = await SeedTestServer.RealTool;

    var first = await tool.BakeAsync(_owner, _cancellationToken);
    var second = await tool.BakeAsync(_owner, _cancellationToken);

    Assert.Equal(
      ["(schema)", .. tool.Tree.InBakeOrder().Select(layer => layer.Name)],
      first.Select(baked => baked.Layer));
    Assert.All(second, baked => Assert.False(baked.Built));
    Assert.All(second, baked => Assert.True(SeedDatabaseNames.IsTemplate(baked.Name)));
  }

  // What the API role may do in a database is the schema template's to say (`SchemaTemplateTests`).
  [Fact]
  public async Task CreateTestDatabaseAsync_ChildLayer_HasTheChainsRowsAndTheAnchorAndItsIdCannotBeUsedAgain() {
    var id = Guid.NewGuid().ToString("N");
    var name = SeedDatabaseNames.Test(id);
    try {
      var created = await _tool.CreateTestDatabaseAsync(_owner, id, FixtureSeeds.Child, _cancellationToken);
      var exception = await Assert.ThrowsAsync<SeedFormatException>(
        () => _tool.CreateTestDatabaseAsync(_owner, id, FixtureSeeds.Child, _cancellationToken));

      await using var clone = await DatabaseEnvironment.OpenOwnerAsync(name, _cancellationToken);
      var tags = await clone.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken);
      // The root's tag with the color the child gave it.
      var color = await clone.QuerySingleAsync<string>(
        "SELECT color FROM tag.tag WHERE slug = 'qux';",
        _cancellationToken);
      var anchors = await clone.QuerySingleAsync<long>("SELECT count(*) FROM seed.hydration;", _cancellationToken);
      var nextId = await clone.QuerySingleAsync<int>(FixtureSeeds.InsertTagReturningId("Foo"), _cancellationToken);
      Assert.Equal(name, created);
      Assert.Equal(FixtureSeeds.ChildRows, tags);
      Assert.Equal("emerald-200", color);
      Assert.Equal(1, anchors);
      Assert.Equal(2001, nextId);
      Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    } finally {
      await _databases.DropAsync(_owner, name, CancellationToken.None);
    }
  }

  [Fact]
  public async Task CreateTestDatabaseAsync_RootLayer_HasOnlyTheRootsRowsAndHandsOutIdsFromTheNextBlock() {
    var id = Guid.NewGuid().ToString("N");
    var name = SeedDatabaseNames.Test(id);
    try {
      _ = await _tool.CreateTestDatabaseAsync(_owner, id, FixtureSeeds.Root, _cancellationToken);

      await using var clone = await DatabaseEnvironment.OpenOwnerAsync(name, _cancellationToken);
      var tags = await clone.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken);
      var nextId = await clone.QuerySingleAsync<int>(FixtureSeeds.InsertTagReturningId("Foo"), _cancellationToken);
      Assert.Equal(FixtureSeeds.RootRows, tags);
      // A tag made in this database is where an export of a child of the root expects its new rows.
      Assert.Equal(1001, nextId);
    } finally {
      await _databases.DropAsync(_owner, name, CancellationToken.None);
    }
  }

  [Fact]
  public async Task CreateTestDatabaseAsync_UnknownLayer_Throws()
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _tool.CreateTestDatabaseAsync(_owner, Guid.NewGuid().ToString("N"), "no-such-layer", _cancellationToken));

  [Fact]
  public async Task SeedDevelopmentAsync_ADatabaseOfItsOwn_IsDroppedAndRebuiltFromTheLayerAtNow() {
    var development = $"seed_tooltest_{Guid.NewGuid():N}";
    var tool = _tool.WithDevelopment(development);
    try {
      _ = await tool.SeedDevelopmentAsync(_owner, FixtureSeeds.Root, editing: false, _cancellationToken);
      await using (var first = await DatabaseEnvironment.OpenOwnerAsync(development, _cancellationToken)) {
        await first.ExecuteAsync(FixtureSeeds.InsertTag("Foo"), _cancellationToken);
      }
      var before = SeedTestServer.Clock.GetCurrentInstant();

      var rows = await tool.SeedDevelopmentAsync(_owner, FixtureSeeds.Child, editing: false, _cancellationToken);

      await using var rebuilt = await DatabaseEnvironment.OpenOwnerAsync(development, _cancellationToken);
      var tags = await rebuilt.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken);
      var anchor = Instant.FromDateTimeUtc(
        await rebuilt.QuerySingleAsync<DateTime>("SELECT anchor_utc FROM seed.hydration;", _cancellationToken));
      Assert.Equal(FixtureSeeds.ChildRows, rows);
      Assert.Equal(FixtureSeeds.ChildRows, tags);
      Assert.True(anchor >= before);
    } finally {
      await _databases.DropDevelopmentAsync(_owner, development, CancellationToken.None);
    }
  }

  [Theory]
  [InlineData("postgres")]
  [InlineData("template1")]
  public async Task SeedDevelopmentAsync_AServerDatabase_ThrowsBeforeDroppingAnything(string development)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _tool.WithDevelopment(development).SeedDevelopmentAsync(
        _owner,
        FixtureSeeds.Root,
        editing: false,
        _cancellationToken));

  [Fact]
  public async Task CleanAsync_StaleDatabasesUnderThePrefix_AreDropped() {
    var prefix = $"seed_cleantest_{Guid.NewGuid():N}_";
    var names = new[] {
      prefix + "foo",
      prefix + "bar_building",
    };
    try {
      foreach (var name in names) {
        await _databases.CreateAsync(_owner, name, _cancellationToken);
      }

      var dropped = await _tool.CleanAsync(_owner, everything: false, prefix, _cancellationToken);

      Assert.Equal(names.Order(StringComparer.Ordinal), dropped);
      foreach (var name in names) {
        Assert.False(await _databases.ExistsAsync(_owner, name, _cancellationToken));
      }
    } finally {
      foreach (var name in names) {
        await _databases.DropAsync(_owner, name, CancellationToken.None);
      }
    }
  }

  [Fact]
  public async Task CleanAsync_TheCurrentSchemaTemplate_IsKeptUnlessEverythingIsAsked() {
    var (current, _) = await SeedTestServer.RealTemplate.EnsureAsync(_owner, _cancellationToken);

    var kept = await _tool.CleanAsync(_owner, everything: false, current, _cancellationToken);

    Assert.Empty(kept);
    Assert.True(await _databases.ExistsAsync(_owner, current, _cancellationToken));
  }

  [Fact]
  public async Task CleanAsync_NotASeedPrefix_Throws()
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _tool.CleanAsync(_owner, everything: true, "josholson", _cancellationToken));

  [Fact]
  public async Task CheckAsync_RealLayers_AllApply()
    => Assert.Empty(await (await SeedTestServer.RealTool).CheckAsync(_owner, _cancellationToken));
}
