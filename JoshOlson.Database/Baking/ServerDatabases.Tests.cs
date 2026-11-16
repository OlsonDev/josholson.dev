using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Makes and drops databases named `seed_serverdb_*` on the dev database's server; nothing touches the dev database
// itself. The source database, which has a table with one row, is made by the first call to `SourceAsync`: half the
// tests never get as far as needing one. Cloning a source somebody is connected to is
// `ServerDatabasesSourceInUseTests`.
public sealed class ServerDatabasesTests : IAsyncLifetime {
  // What a connection to a database with `ALLOW_CONNECTIONS false` gets.
  private const string _NotAcceptingConnections = "55000";

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _source = $"seed_serverdb_{Guid.NewGuid():N}";
  private readonly string _target = $"seed_serverdb_{Guid.NewGuid():N}";
  private readonly ServerDatabases _databases = SeedTestServer.Databases;
  private bool _sourceMade;

  public ValueTask InitializeAsync()
    => ValueTask.CompletedTask;

  public async ValueTask DisposeAsync() {
    await _databases.DropAsync(SeedDatabaseNames.Building(_target), CancellationToken.None);
    await _databases.DropAsync(_target, CancellationToken.None);
    if (_sourceMade) {
      await _databases.DropAsync(_source, CancellationToken.None);
    }
  }

  private async Task<string> SourceAsync() {
    if (!_sourceMade) {
      await _databases.CreateAsync(_source, _cancellationToken);
      _sourceMade = true;
      await using var source = await DatabaseEnvironment.OpenOwnerAsync(_source, _cancellationToken);
      await source.ExecuteAsync("CREATE TABLE foo (bar INT); INSERT INTO foo (bar) VALUES (42);", _cancellationToken);
    }
    return _source;
  }

  [Fact]
  public async Task ExistsAsync_MadeThenDropped_FollowsIt() {
    var before = await _databases.ExistsAsync(_target, _cancellationToken);
    await _databases.CreateAsync(_target, _cancellationToken);
    var made = await _databases.ExistsAsync(_target, _cancellationToken);
    await _databases.DropAsync(_target, _cancellationToken);
    var dropped = await _databases.ExistsAsync(_target, _cancellationToken);

    Assert.False(before);
    Assert.True(made);
    Assert.False(dropped);
  }

  [Fact]
  public async Task DropAsync_MissingDatabase_DoesNothing()
    => await _databases.DropAsync(_target, _cancellationToken);

  [Fact]
  public async Task DropAsync_SomebodyConnected_DropsItAnyway() {
    var source = await SourceAsync();
    await using var holder = await DatabaseEnvironment.OpenOwnerAsync(source, _cancellationToken);

    await _databases.DropAsync(source, _cancellationToken);

    Assert.False(await _databases.ExistsAsync(source, _cancellationToken));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("postgres")]
  public async Task DropAsync_OutsideThePrefix_Throws(string name)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.DropAsync(name, _cancellationToken));

  [Theory]
  [InlineData("postgres")]
  [InlineData("template0")]
  [InlineData("template1")]
  public async Task DropDevelopmentAsync_ServersOwnDatabase_Throws(string name)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.DropDevelopmentAsync(name, _cancellationToken));

  [Fact]
  public async Task ListAsync_Prefix_ReturnsTheMatchesInOrder() {
    var source = await SourceAsync();
    await _databases.CreateAsync(_target, _cancellationToken);

    var names = await _databases.ListAsync("seed_serverdb_", _cancellationToken);

    Assert.Contains(source, names);
    Assert.Contains(_target, names);
    Assert.Equal(names.Order(StringComparer.Ordinal), names);
    Assert.All(names, name => Assert.StartsWith("seed_serverdb_", name, StringComparison.Ordinal));
  }

  [Fact]
  public async Task ListAsync_PrefixOutsideTheSeedTool_Throws()
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.ListAsync("jos", _cancellationToken));

  [Fact]
  public async Task CloseAsync_Database_RefusesConnections() {
    var source = await SourceAsync();

    await _databases.CloseAsync(source, _cancellationToken);

    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => DatabaseEnvironment.OpenOwnerAsync(source, _cancellationToken));
    Assert.Equal(_NotAcceptingConnections, exception.SqlState);
  }

  [Fact]
  public async Task RenameAsync_Database_KeepsItsRowsUnderTheNewName() {
    var source = await SourceAsync();

    await _databases.RenameAsync(source, _target, _cancellationToken);

    await using var renamed = await DatabaseEnvironment.OpenOwnerAsync(_target, _cancellationToken);
    Assert.Equal(42, await renamed.QuerySingleAsync<int>("SELECT bar FROM foo;", _cancellationToken));
    Assert.False(await _databases.ExistsAsync(source, _cancellationToken));
  }

  [Fact]
  public async Task CloneAsync_Source_CopiesItsRows() {
    await _databases.CloneAsync(await SourceAsync(), _target, _cancellationToken);

    await using var clone = await DatabaseEnvironment.OpenOwnerAsync(_target, _cancellationToken);
    Assert.Equal(42, await clone.QuerySingleAsync<int>("SELECT bar FROM foo;", _cancellationToken));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("postgres")]
  public async Task CloneAsync_TargetOutsideThePrefix_Throws(string target)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.CloneAsync(_source, target, _cancellationToken));

  [Fact]
  public async Task EnsureTemplateAsync_Missing_BuildsItClosedAndOnlyOnce() {
    var source = await SourceAsync();
    var builds = new List<string>();
    Task Build(string building) {
      builds.Add(building);
      return _databases.CloneAsync(source, building, _cancellationToken);
    }

    var first = await _databases.EnsureTemplateAsync(_target, Build, _cancellationToken);
    var second = await _databases.EnsureTemplateAsync(_target, Build, _cancellationToken);

    Assert.True(first);
    Assert.False(second);
    Assert.Equal([SeedDatabaseNames.Building(_target)], builds);
    Assert.True(await _databases.ExistsAsync(_target, _cancellationToken));
    Assert.False(await _databases.ExistsAsync(SeedDatabaseNames.Building(_target), _cancellationToken));
    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => DatabaseEnvironment.OpenOwnerAsync(_target, _cancellationToken));
    Assert.Equal(_NotAcceptingConnections, exception.SqlState);
  }

  [Fact]
  public async Task EnsureTemplateAsync_TwoCallersAtOnce_BuildItOnce() {
    var source = await SourceAsync();
    var builds = 0;
    // Each call takes the lock on a connection of its own, so two at once are two sessions, as two processes would be.
    async Task BuildAsync(string building) {
      _ = Interlocked.Increment(ref builds);
      await Task.Delay(300, _cancellationToken);
      await _databases.CloneAsync(source, building, _cancellationToken);
    }

    var results = await Task.WhenAll(
      _databases.EnsureTemplateAsync(_target, BuildAsync, _cancellationToken),
      _databases.EnsureTemplateAsync(_target, BuildAsync, _cancellationToken));

    Assert.Equal(1, builds);
    Assert.Equal([false, true], results.Order());
  }

  [Fact]
  public async Task EnsureTemplateAsync_BuildFails_LeavesNothingBehind() {
    var source = await SourceAsync();
    async Task BuildThenFail(string building) {
      await _databases.CloneAsync(source, building, _cancellationToken);
      throw new InvalidOperationException("foo");
    }

    _ = await Assert.ThrowsAsync<InvalidOperationException>(
      () => _databases.EnsureTemplateAsync(_target, BuildThenFail, _cancellationToken));

    Assert.False(await _databases.ExistsAsync(_target, _cancellationToken));
    Assert.False(await _databases.ExistsAsync(SeedDatabaseNames.Building(_target), _cancellationToken));
  }

  [Fact]
  public async Task EnsureTemplateAsync_EarlierBuildDiedHalfway_StartsOverFromNothing() {
    var source = await SourceAsync();
    await _databases.CreateAsync(SeedDatabaseNames.Building(_target), _cancellationToken);

    var built = await _databases.EnsureTemplateAsync(
      _target,
      building => _databases.CloneAsync(source, building, _cancellationToken),
      _cancellationToken);

    Assert.True(built);
    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => DatabaseEnvironment.OpenOwnerAsync(_target, _cancellationToken));
    Assert.Equal(_NotAcceptingConnections, exception.SqlState);
  }
}
