using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Bakes and clones the real layers. The templates it builds are the real ones and stay for the next run. Everything
// else it makes is named `seed_tooltest_*` or `seed_cleantest_*` and dropped at the end, and the dev
// database itself is never touched: the rebuild test points the tool at a database of its own.
public sealed class SeedToolTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ServerDatabases _databases = new();

  private async Task<SeedTool> OpenToolAsync()
    => await SeedTool.OpenAsync(SeedPaths.ForRepository(), SystemClock.Instance, _cancellationToken);

  private async Task<NpgsqlConnection> OpenOwnerAsync() {
    var owner = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await owner.OpenAsync(_cancellationToken);
    return owner;
  }

  private static async Task<NpgsqlConnection> OpenAsync(string database, CancellationToken cancellationToken) {
    var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(database));
    await connection.OpenAsync(cancellationToken);
    return connection;
  }

  [Fact]
  public async Task BakeAsync_RealLayers_BuildsEachTemplateOnce() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();

    var first = await tool.BakeAsync(owner, _cancellationToken);
    var second = await tool.BakeAsync(owner, _cancellationToken);

    Assert.Equal(["(schema)", .. tool.Tree.InBakeOrder().Select(layer => layer.Name)], first.Select(baked => baked.Layer));
    Assert.All(second, baked => Assert.False(baked.Built));
    Assert.All(second, baked => Assert.True(SeedDatabaseNames.IsTemplate(baked.Name)));
  }

  [Fact]
  public async Task CreateTestDatabaseAsync_ChildLayer_HasTheChainsRowsTheAnchorAndTheApiRolesGrants() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();
    var id = Guid.NewGuid().ToString("N");
    var name = SeedDatabaseNames.Test(id);
    try {
      var created = await tool.CreateTestDatabaseAsync(owner, id, SeedLayers.MoreTags, _cancellationToken);

      await using var clone = await OpenAsync(name, _cancellationToken);
      var tags = await clone.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken);
      var color = await clone.QuerySingleAsync<string>("SELECT color FROM tag.tag WHERE slug = 'database';", _cancellationToken);
      var anchors = await clone.QuerySingleAsync<long>("SELECT count(*) FROM seed.hydration;", _cancellationToken);
      var nextId = await clone.QuerySingleAsync<int>(
        "INSERT INTO tag.tag (name, slug) VALUES ('Foo', 'foo') RETURNING tag_id;",
        _cancellationToken);
      var apiCanConnect = await clone.QuerySingleAsync<bool>(
        "SELECT has_database_privilege('josholson_api', current_database(), 'CONNECT');",
        _cancellationToken);
      var apiCanReadTags = await clone.QuerySingleAsync<bool>(
        "SELECT has_table_privilege('josholson_api', 'tag.tag', 'SELECT');",
        _cancellationToken);
      var apiSeesSeedSchema = await clone.QuerySingleAsync<bool>(
        "SELECT has_schema_privilege('josholson_api', 'seed', 'USAGE');",
        _cancellationToken);
      Assert.Equal(name, created);
      Assert.Equal(22, tags);
      Assert.Equal("indigo-200", color);
      Assert.Equal(1, anchors);
      Assert.Equal(2001, nextId);
      Assert.True(apiCanConnect);
      Assert.True(apiCanReadTags);
      Assert.False(apiSeesSeedSchema);
    } finally {
      await _databases.DropAsync(owner, name, CancellationToken.None);
    }
  }

  [Fact]
  public async Task CreateTestDatabaseAsync_RootLayer_HasOnlyTheRootsRowsAndHandsOutIdsFromTheNextBlock() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();
    var id = Guid.NewGuid().ToString("N");
    var name = SeedDatabaseNames.Test(id);
    try {
      _ = await tool.CreateTestDatabaseAsync(owner, id, SeedLayers.Minimal, _cancellationToken);

      await using var clone = await OpenAsync(name, _cancellationToken);
      Assert.Equal(3, await clone.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken));
      // A tag made in this database is where an export of a child of `minimal` expects its new rows.
      Assert.Equal(
        1001,
        await clone.QuerySingleAsync<int>(
          "INSERT INTO tag.tag (name, slug) VALUES ('Foo', 'foo') RETURNING tag_id;",
          _cancellationToken));
    } finally {
      await _databases.DropAsync(owner, name, CancellationToken.None);
    }
  }

  [Fact]
  public async Task CreateTestDatabaseAsync_IdInUse_Throws() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();
    var id = Guid.NewGuid().ToString("N");
    var name = SeedDatabaseNames.Test(id);
    try {
      _ = await tool.CreateTestDatabaseAsync(owner, id, SeedLayers.Minimal, _cancellationToken);

      var exception = await Assert.ThrowsAsync<SeedFormatException>(
        () => tool.CreateTestDatabaseAsync(owner, id, SeedLayers.Minimal, _cancellationToken));

      Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    } finally {
      await _databases.DropAsync(owner, name, CancellationToken.None);
    }
  }

  [Fact]
  public async Task CreateTestDatabaseAsync_UnknownLayer_Throws() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();

    _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => tool.CreateTestDatabaseAsync(owner, Guid.NewGuid().ToString("N"), "no-such-layer", _cancellationToken));
  }

  [Fact]
  public async Task SeedDevelopmentAsync_ADatabaseOfItsOwn_IsDroppedAndRebuiltFromTheLayerAtNow() {
    var development = $"seed_tooltest_{Guid.NewGuid():N}";
    var tool = (await OpenToolAsync()).WithDevelopment(development);
    await using var owner = await OpenOwnerAsync();
    try {
      _ = await tool.SeedDevelopmentAsync(owner, SeedLayers.Minimal, editing: false, _cancellationToken);
      await using (var first = await OpenAsync(development, _cancellationToken)) {
        await first.ExecuteAsync("INSERT INTO tag.tag (name, slug) VALUES ('Foo', 'foo');", _cancellationToken);
      }
      var before = SystemClock.Instance.GetCurrentInstant();

      var rows = await tool.SeedDevelopmentAsync(owner, SeedLayers.MoreTags, editing: false, _cancellationToken);

      await using var rebuilt = await OpenAsync(development, _cancellationToken);
      var tags = await rebuilt.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken);
      var anchor = Instant.FromDateTimeUtc(
        await rebuilt.QuerySingleAsync<DateTime>("SELECT anchor_utc FROM seed.hydration;", _cancellationToken));
      Assert.Equal(22, rows);
      Assert.Equal(22, tags);
      Assert.True(anchor >= before);
    } finally {
      await _databases.DropDevelopmentAsync(owner, development, CancellationToken.None);
    }
  }

  [Theory]
  [InlineData("postgres")]
  [InlineData("template1")]
  public async Task SeedDevelopmentAsync_AServerDatabase_ThrowsBeforeDroppingAnything(string development) {
    var tool = (await OpenToolAsync()).WithDevelopment(development);
    await using var owner = await OpenOwnerAsync();

    _ = await Assert.ThrowsAsync<SeedFormatException>(() => tool.SeedDevelopmentAsync(owner, SeedLayers.Minimal, editing: false, _cancellationToken));
  }

  [Fact]
  public async Task CleanAsync_StaleDatabasesUnderThePrefix_AreDropped() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();
    var prefix = $"seed_cleantest_{Guid.NewGuid():N}_";
    var names = new[] { prefix + "foo", prefix + "bar_building" };
    try {
      foreach (var name in names) {
        await _databases.CreateAsync(owner, name, _cancellationToken);
      }

      var dropped = await tool.CleanAsync(owner, everything: false, prefix, _cancellationToken);

      Assert.Equal(names.Order(StringComparer.Ordinal), dropped);
      foreach (var name in names) {
        Assert.False(await _databases.ExistsAsync(owner, name, _cancellationToken));
      }
    } finally {
      foreach (var name in names) {
        await _databases.DropAsync(owner, name, CancellationToken.None);
      }
    }
  }

  [Fact]
  public async Task CleanAsync_TheCurrentSchemaTemplate_IsKeptUnlessEverythingIsAsked() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();
    var baked = await tool.BakeAsync(owner, _cancellationToken);
    var current = baked[0].Name;

    var kept = await tool.CleanAsync(owner, everything: false, current, _cancellationToken);

    Assert.Empty(kept);
    Assert.True(await _databases.ExistsAsync(owner, current, _cancellationToken));
  }

  [Fact]
  public async Task CleanAsync_NotASeedPrefix_Throws() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();

    _ = await Assert.ThrowsAsync<SeedFormatException>(() => tool.CleanAsync(owner, everything: true, "josholson", _cancellationToken));
  }

  [Fact]
  public async Task CheckAsync_RealLayers_AllApply() {
    var tool = await OpenToolAsync();
    await using var owner = await OpenOwnerAsync();

    Assert.Empty(await tool.CheckAsync(owner, _cancellationToken));
  }
}
