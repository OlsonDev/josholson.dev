using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Cloning a source database that somebody is connected to, which Postgres refuses and `ServerDatabases` tries again.
// The tests are apart from `ServerDatabasesTests` so the two classes run side by side. The databases are named
// `seed_serverdb_*` and dropped at the end.
public sealed class ServerDatabasesSourceInUseTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _source = $"seed_serverdb_{Guid.NewGuid():N}";
  private readonly string _target = $"seed_serverdb_{Guid.NewGuid():N}";
  private readonly ServerDatabases _databases = SeedTestServer.Databases;
  private NpgsqlConnection _owner = null!;

  public async ValueTask InitializeAsync() {
    _owner = await DatabaseEnvironment.OpenOwnerAsync(_cancellationToken);
    await _databases.CreateAsync(_owner, _source, _cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    await _databases.DropAsync(_owner, _target, CancellationToken.None);
    await _databases.DropAsync(_owner, _source, CancellationToken.None);
    await _owner.DisposeAsync();
  }

  // Asks the server to read its configuration files again, over and over on a connection of its own, until `waiting`
  // is done. Nothing in them has changed, so all it does is signal every backend (and write a line in the server's
  // log each time).
  private async Task ReloadConfigurationUntilAsync(Task waiting) {
    await using var other = await SeedTestServer.OpenMaintenanceAsync(_cancellationToken);
    while (!waiting.IsCompleted) {
      _ = await other.QuerySingleAsync<bool>("SELECT pg_reload_conf();", _cancellationToken);
    }
  }

  [Fact]
  public async Task CloneAsync_SourceStaysInUse_GivesUpAfterTheAttempts() {
    await using var holder = await DatabaseEnvironment.OpenOwnerAsync(_source, _cancellationToken);
    // Postgres itself waits inside each attempt to clone a source somebody is connected to, so one attempt is all this
    // needs to see the error given up on; the test below has the retrying.
    var databases = new ServerDatabases {
      Attempts = 1,
      Delay = TimeSpan.FromMilliseconds(10),
    };

    // That wait is 50 looks at who is connected, with a sleep of 100ms after each: five seconds. A signal ends a sleep
    // early, and every database made or dropped on the server signals every backend, which is why this test took one
    // second beside the other classes and five alone. So it has the server signal them while the clone waits, which
    // takes the wait down to a few hundredths of a second. If a later Postgres waits another way, the test still
    // passes, at five seconds.
    var cloning = databases.CloneAsync(_owner, _source, _target, _cancellationToken);
    await ReloadConfigurationUntilAsync(cloning);
    var exception = await Assert.ThrowsAsync<PostgresException>(() => cloning);

    Assert.Equal("55006", exception.SqlState);
  }

  [Fact]
  public async Task CloneAsync_SourceFreedWhileWaiting_Succeeds() {
    var holder = await DatabaseEnvironment.OpenOwnerAsync(_source, _cancellationToken);
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
}
