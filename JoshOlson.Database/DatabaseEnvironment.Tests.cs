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
}
