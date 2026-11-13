using System.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Database;

// The tests run through mise, which loads `.env`, so POSTGRES_PASSWORD is set.
public class DatabaseEnvironmentTests {
  [Fact]
  public void OwnerConnectionString_ReadsThePostgresSettingsAndTurnsSslOff() {
    var builder = new NpgsqlConnectionStringBuilder(DatabaseEnvironment.OwnerConnectionString());

    Assert.Equal("127.0.0.1", builder.Host);
    Assert.Equal(SslMode.Disable, builder.SslMode);
    Assert.Equal(Environment.GetEnvironmentVariable("POSTGRES_PASSWORD"), builder.Password);
    Assert.False(string.IsNullOrEmpty(builder.Database));
  }

  [Fact]
  public async Task OpenOwnerAsync_NoDatabase_IsOpenOnTheDevDatabase() {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(TestContext.Current.CancellationToken);

    Assert.Equal(ConnectionState.Open, connection.State);
    Assert.Equal(DatabaseEnvironment.DevelopmentDatabase(), connection.Database);
  }

  [Fact]
  public async Task OpenOwnerAsync_Database_IsOpenOnItWithoutPooling() {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync("postgres", TestContext.Current.CancellationToken);

    Assert.Equal(ConnectionState.Open, connection.State);
    Assert.Equal("postgres", connection.Database);
    Assert.False(new NpgsqlConnectionStringBuilder(connection.ConnectionString).Pooling);
  }

  [Fact]
  public async Task OpenOwnerAsync_DatabaseThatIsNotThere_Throws() {
    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => DatabaseEnvironment.OpenOwnerAsync($"seed_missing_{Guid.NewGuid():N}", TestContext.Current.CancellationToken));

    Assert.Equal(PostgresErrorCodes.InvalidCatalogName, exception.SqlState);
  }
}
