using JoshOlson.Database;
using JoshOlson.Database.Baking;
using Npgsql;
using Xunit;

namespace JoshOlson.Seed.Commands;

// Works on a prefix of its own, so it drops nothing another test is using: `clean` with the default prefix would drop
// every stale `seed_` database on the server, other tests' included.
public sealed class CleanCommandTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _prefix = $"seed_cleancmd_{Guid.NewGuid():N}_";
  private readonly ServerDatabases _databases = new();
  private NpgsqlConnection _owner = null!;

  public async ValueTask InitializeAsync() {
    _owner = new(DatabaseEnvironment.OwnerConnectionString("postgres"));
    await _owner.OpenAsync(_cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    await _databases.DropAsync(_owner, _prefix + "foo", CancellationToken.None);
    await _databases.DropAsync(_owner, _prefix + "bar_building", CancellationToken.None);
    await _owner.DisposeAsync();
  }

  [Fact]
  public async Task Execute_StaleDatabasesUnderThePrefix_AreDroppedAndCounted() {
    await _databases.CreateAsync(_owner, _prefix + "foo", _cancellationToken);
    await _databases.CreateAsync(_owner, _prefix + "bar_building", _cancellationToken);

    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "clean", "--prefix", _prefix);

    Assert.True(exitCode is 0, output);
    Assert.Contains($"Dropped {_prefix}foo", output, StringComparison.Ordinal);
    Assert.Contains($"Dropped {_prefix}bar_building", output, StringComparison.Ordinal);
    Assert.Contains("2 database(s) dropped.", output, StringComparison.Ordinal);
    Assert.False(await _databases.ExistsAsync(_owner, _prefix + "foo", _cancellationToken));
    Assert.False(await _databases.ExistsAsync(_owner, _prefix + "bar_building", _cancellationToken));
  }

  [Fact]
  public async Task Execute_NothingUnderThePrefix_SaysZero() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "clean", "--prefix", _prefix);

    Assert.Equal(0, exitCode);
    Assert.Contains("0 database(s) dropped.", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_PrefixOutsideTheSeedTool_FailsWithExitOne() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "clean", "--prefix", "josholson");

    Assert.Equal(1, exitCode);
    Assert.Contains("not a seed tool database name", output, StringComparison.Ordinal);
  }
}
