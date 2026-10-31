using Dapper;
using JoshOlson.Api.Common.Extensions;
using JoshOlson.Api.Common.Testing;
using NodaTime;
using Xunit;

namespace JoshOlson.Api.Common.Db;

public class DapperConfigurationTests {
  private sealed record Row {
    public required int FooBar { get; init; }
    public required Instant? BazQux { get; init; }
  }

  [Fact]
  public async Task Instant_RoundTripsAsParameterAndResult() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var instant = Instant.FromUtc(2026, 10, 30, 12, 34, 56);
    var parameters = new { instant };
    var sql = "SELECT @instant::timestamptz;";

    Assert.Equal(instant, await connection.QuerySingleAsync<Instant>(sql, parameters, cancellationToken));
  }

  [Fact]
  public async Task NullableInstant_RoundTripsNullAsParameterAndMember() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var parameters = new { instant = (Instant?)null };
    var sql = "SELECT 7 AS foo_bar, @instant::timestamptz AS baz_qux;";

    var row = await connection.QuerySingleAsync<Row>(sql, parameters, cancellationToken);

    Assert.Equal(7, row.FooBar);
    Assert.Null(row.BazQux);
  }

  [Fact]
  public async Task Columns_MatchPropertiesIgnoringUnderscores() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var instant = Instant.FromUtc(2026, 10, 30, 12, 34, 56);
    var parameters = new { instant };
    var sql = "SELECT 7 AS foo_bar, @instant::timestamptz AS baz_qux;";

    var row = await connection.QuerySingleAsync<Row>(sql, parameters, cancellationToken);

    Assert.Equal(7, row.FooBar);
    Assert.Equal(instant, row.BazQux);
  }
}
