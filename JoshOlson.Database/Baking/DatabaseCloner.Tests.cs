using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Baking;

// Makes and drops databases named `seed_cloner_*` on the dev database's server; nothing touches the dev database
// itself.
[Collection(TemplateCollection.Name)]
public sealed class DatabaseClonerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _source = $"seed_cloner_{Guid.NewGuid():N}";
  private readonly string _target = $"seed_cloner_{Guid.NewGuid():N}";
  private NpgsqlConnection _owner = null!;

  public async ValueTask InitializeAsync() {
    _owner = new(DatabaseEnvironment.OwnerConnectionString());
    await _owner.OpenAsync(_cancellationToken);
    await _owner.ExecuteAsync($"CREATE DATABASE {SeedDatabaseNames.Quote(_source)};", null, _cancellationToken);
    await using var source = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    await source.OpenAsync(_cancellationToken);
    await source.ExecuteAsync("CREATE TABLE foo (bar INT); INSERT INTO foo (bar) VALUES (42);", null, _cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    await SchemaTemplate.DropAsync(_owner, _target, CancellationToken.None);
    await SchemaTemplate.DropAsync(_owner, _source, CancellationToken.None);
    await _owner.DisposeAsync();
  }

  [Fact]
  public async Task CloneAsync_Source_CopiesItsRows() {
    await new DatabaseCloner().CloneAsync(_owner, _source, _target, _cancellationToken);

    await using var clone = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_target));
    await clone.OpenAsync(_cancellationToken);
    Assert.Equal(42, await clone.QuerySingleAsync<int>("SELECT bar FROM foo;", null, _cancellationToken));
  }

  [Fact]
  public async Task CloneAsync_SourceStaysInUse_GivesUpAfterTheAttempts() {
    await using var holder = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    await holder.OpenAsync(_cancellationToken);
    var cloner = new DatabaseCloner {
      Attempts = 3,
      Delay = TimeSpan.FromMilliseconds(10),
    };

    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => cloner.CloneAsync(_owner, _source, _target, _cancellationToken));

    Assert.Equal("55006", exception.SqlState);
  }

  [Fact]
  public async Task CloneAsync_SourceFreedWhileWaiting_Succeeds() {
    var holder = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(_source));
    await holder.OpenAsync(_cancellationToken);
    var cloner = new DatabaseCloner {
      Attempts = 200,
      Delay = TimeSpan.FromMilliseconds(25),
    };

    var cloning = cloner.CloneAsync(_owner, _source, _target, _cancellationToken);
    await Task.Delay(150, _cancellationToken);
    await holder.DisposeAsync();
    await cloning;

    Assert.True(await SchemaTemplate.ExistsAsync(_owner, _target, _cancellationToken));
  }

  [Theory]
  [InlineData("josholson")]
  [InlineData("postgres")]
  public async Task CloneAsync_TargetOutsideThePrefix_Throws(string target)
    => _ = await Assert.ThrowsAsync<SeedFormatException>(
      () => new DatabaseCloner().CloneAsync(_owner, _source, target, _cancellationToken));
}
