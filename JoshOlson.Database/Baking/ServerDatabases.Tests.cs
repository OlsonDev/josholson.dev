using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Makes and drops databases named `seed_serverdb_*` on the dev database's server; nothing touches the dev database
// itself.
[Collection(TemplateCollection.Name)]
public sealed class ServerDatabasesTests : IAsyncLifetime {
  // What a connection to a database with `ALLOW_CONNECTIONS false` gets.
  private const string _NotAcceptingConnections = "55000";

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _source = $"seed_serverdb_{Guid.NewGuid():N}";
  private readonly string _target = $"seed_serverdb_{Guid.NewGuid():N}";
  private readonly ServerDatabases _databases = new();
  private NpgsqlConnection _owner = null!;

  public async ValueTask InitializeAsync() {
    _owner = new(DatabaseEnvironment.OwnerConnectionString());
    await _owner.OpenAsync(_cancellationToken);
    await _databases.CreateAsync(_owner, _source, _cancellationToken);
    await using var source = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    await source.OpenAsync(_cancellationToken);
    await source.ExecuteAsync("CREATE TABLE foo (bar INT); INSERT INTO foo (bar) VALUES (42);", _cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    await _databases.DropAsync(_owner, SeedDatabaseNames.Building(_target), CancellationToken.None);
    await _databases.DropAsync(_owner, _target, CancellationToken.None);
    await _databases.DropAsync(_owner, _source, CancellationToken.None);
    await _owner.DisposeAsync();
  }

  [Fact]
  public async Task ExistsAsync_MadeThenDropped_FollowsIt() {
    var before = await _databases.ExistsAsync(_owner, _target, _cancellationToken);
    await _databases.CreateAsync(_owner, _target, _cancellationToken);
    var made = await _databases.ExistsAsync(_owner, _target, _cancellationToken);
    await _databases.DropAsync(_owner, _target, _cancellationToken);
    var dropped = await _databases.ExistsAsync(_owner, _target, _cancellationToken);

    Assert.False(before);
    Assert.True(made);
    Assert.False(dropped);
  }

  [Fact]
  public async Task DropAsync_MissingDatabase_DoesNothing()
    => await _databases.DropAsync(_owner, _target, _cancellationToken);

  [Fact]
  public async Task DropAsync_SomebodyConnected_DropsItAnyway() {
    await using var holder = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    await holder.OpenAsync(_cancellationToken);

    await _databases.DropAsync(_owner, _source, _cancellationToken);

    Assert.False(await _databases.ExistsAsync(_owner, _source, _cancellationToken));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("postgres")]
  public async Task DropAsync_OutsideThePrefix_Throws(string name)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.DropAsync(_owner, name, _cancellationToken));

  [Theory]
  [InlineData("postgres")]
  [InlineData("template0")]
  [InlineData("template1")]
  public async Task DropDevelopmentAsync_ServersOwnDatabase_Throws(string name)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.DropDevelopmentAsync(_owner, name, _cancellationToken));

  [Fact]
  public async Task ListAsync_Prefix_ReturnsTheMatchesInOrder() {
    await _databases.CreateAsync(_owner, _target, _cancellationToken);

    var names = await _databases.ListAsync(_owner, "seed_serverdb_", _cancellationToken);

    Assert.Contains(_source, names);
    Assert.Contains(_target, names);
    Assert.Equal(names.Order(StringComparer.Ordinal), names);
    Assert.All(names, name => Assert.StartsWith("seed_serverdb_", name, StringComparison.Ordinal));
  }

  [Fact]
  public async Task ListAsync_PrefixOutsideTheSeedTool_Throws()
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.ListAsync(_owner, "jos", _cancellationToken));

  [Fact]
  public async Task CloseAsync_Database_RefusesConnections() {
    await _databases.CloseAsync(_owner, _source, _cancellationToken);

    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync(_cancellationToken));
    Assert.Equal(_NotAcceptingConnections, exception.SqlState);
  }

  [Fact]
  public async Task RenameAsync_Database_KeepsItsRowsUnderTheNewName() {
    await _databases.RenameAsync(_owner, _source, _target, _cancellationToken);

    await using var renamed = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_target));
    await renamed.OpenAsync(_cancellationToken);
    Assert.Equal(42, await renamed.QuerySingleAsync<int>("SELECT bar FROM foo;", _cancellationToken));
    Assert.False(await _databases.ExistsAsync(_owner, _source, _cancellationToken));
  }

  [Fact]
  public async Task CloneAsync_Source_CopiesItsRows() {
    await _databases.CloneAsync(_owner, _source, _target, _cancellationToken);

    await using var clone = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_target));
    await clone.OpenAsync(_cancellationToken);
    Assert.Equal(42, await clone.QuerySingleAsync<int>("SELECT bar FROM foo;", _cancellationToken));
  }

  [Fact]
  public async Task CloneAsync_SourceStaysInUse_GivesUpAfterTheAttempts() {
    await using var holder = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    await holder.OpenAsync(_cancellationToken);
    var databases = new ServerDatabases {
      Attempts = 3,
      Delay = TimeSpan.FromMilliseconds(10),
    };

    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => databases.CloneAsync(_owner, _source, _target, _cancellationToken));

    Assert.Equal("55006", exception.SqlState);
  }

  [Fact]
  public async Task CloneAsync_SourceFreedWhileWaiting_Succeeds() {
    var holder = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    await holder.OpenAsync(_cancellationToken);
    var databases = new ServerDatabases {
      Attempts = 200,
      Delay = TimeSpan.FromMilliseconds(25),
    };

    var cloning = databases.CloneAsync(_owner, _source, _target, _cancellationToken);
    await Task.Delay(150, _cancellationToken);
    await holder.DisposeAsync();
    await cloning;

    Assert.True(await _databases.ExistsAsync(_owner, _target, _cancellationToken));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("postgres")]
  public async Task CloneAsync_TargetOutsideThePrefix_Throws(string target)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => _databases.CloneAsync(_owner, _source, target, _cancellationToken));

  [Fact]
  public async Task EnsureTemplateAsync_Missing_BuildsItClosedAndOnlyOnce() {
    var builds = new List<string>();
    Task Build(string building) {
      builds.Add(building);
      return _databases.CloneAsync(_owner, _source, building, _cancellationToken);
    }

    var first = await _databases.EnsureTemplateAsync(_owner, _target, Build, _cancellationToken);
    var second = await _databases.EnsureTemplateAsync(_owner, _target, Build, _cancellationToken);

    Assert.True(first);
    Assert.False(second);
    Assert.Equal([SeedDatabaseNames.Building(_target)], builds);
    Assert.True(await _databases.ExistsAsync(_owner, _target, _cancellationToken));
    Assert.False(await _databases.ExistsAsync(_owner, SeedDatabaseNames.Building(_target), _cancellationToken));
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_target));
    var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync(_cancellationToken));
    Assert.Equal(_NotAcceptingConnections, exception.SqlState);
  }

  [Fact]
  public async Task EnsureTemplateAsync_TwoConnectionsToDifferentDatabasesAtOnce_BuildItOnce() {
    var builds = 0;
    // Each caller builds through its own connection, as the commands and the tests do. The test's owner is connected to
    // the dev database and the other to `postgres`: a lock that belonged to one database would let both build.
    Func<string, Task> BuildThrough(NpgsqlConnection connection)
      => async building => {
        _ = Interlocked.Increment(ref builds);
        await Task.Delay(300, _cancellationToken);
        await _databases.CloneAsync(connection, _source, building, _cancellationToken);
      };
    await using var other = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await other.OpenAsync(_cancellationToken);

    var results = await Task.WhenAll(
      _databases.EnsureTemplateAsync(_owner, _target, BuildThrough(_owner), _cancellationToken),
      _databases.EnsureTemplateAsync(other, _target, BuildThrough(other), _cancellationToken));

    Assert.Equal(1, builds);
    Assert.Equal([false, true], results.Order());
  }

  [Fact]
  public async Task EnsureTemplateAsync_BuildFails_LeavesNothingBehind() {
    async Task BuildThenFail(string building) {
      await _databases.CloneAsync(_owner, _source, building, _cancellationToken);
      throw new InvalidOperationException("foo");
    }

    _ = await Assert.ThrowsAsync<InvalidOperationException>(
      () => _databases.EnsureTemplateAsync(_owner, _target, BuildThenFail, _cancellationToken));

    Assert.False(await _databases.ExistsAsync(_owner, _target, _cancellationToken));
    Assert.False(await _databases.ExistsAsync(_owner, SeedDatabaseNames.Building(_target), _cancellationToken));
  }

  [Fact]
  public async Task EnsureTemplateAsync_EarlierBuildDiedHalfway_StartsOverFromNothing() {
    await _databases.CreateAsync(_owner, SeedDatabaseNames.Building(_target), _cancellationToken);

    var built = await _databases.EnsureTemplateAsync(
      _owner,
      _target,
      building => _databases.CloneAsync(_owner, _source, building, _cancellationToken),
      _cancellationToken);

    Assert.True(built);
    await using var template = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_target));
    var exception = await Assert.ThrowsAsync<PostgresException>(() => template.OpenAsync(_cancellationToken));
    Assert.Equal(_NotAcceptingConnections, exception.SqlState);
  }
}
