using System.Net;
using Dapper;
using JoshOlson.Api.Common.Extensions;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Common.Db;

public class DapperConfigurationTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly NpgsqlConnection _connection;

  public DapperConfigurationTests(NpgsqlConnection connection)
    => _connection = connection;

  private sealed record Row {
    public required int FooBar { get; init; }
    public required Instant? BazQux { get; init; }
  }

  [Fact]
  public async Task Instant_RoundTripsAsParameterAndResult() {
    var instant = Instant.FromUtc(2026, 10, 30, 12, 34, 56);
    var parameters = new { instant };
    var sql = """
      SELECT
          @instant::timestamptz  AS instant
      FROM (SELECT) AS _;
      """;

    Assert.Equal(instant, await _connection.QuerySingleAsync<Instant>(sql, parameters, _cancellationToken));
  }

  [Fact]
  public async Task NullableInstant_RoundTripsNullAsParameterAndMember() {
    var parameters = new { instant = (Instant?)null };
    var sql = """
      SELECT
          7                      AS foo_bar
        , @instant::timestamptz  AS baz_qux
      FROM (SELECT) AS _;
      """;

    var row = await _connection.QuerySingleAsync<Row>(sql, parameters, _cancellationToken);

    Assert.Equal(7, row.FooBar);
    Assert.Null(row.BazQux);
  }

  [Fact]
  public async Task IpAddress_RoundTripsAsParameterAndResult() {
    var ip = IPAddress.Parse("192.0.2.7");
    var sql = """
      SELECT
          @ip::inet  AS ip
      FROM (SELECT) AS _;
      """;

    Assert.Equal(ip, await _connection.QuerySingleAsync<IPAddress>(sql, new { ip }, _cancellationToken));
    Assert.Null(await _connection.QuerySingleAsync<IPAddress?>(sql, new { ip = (IPAddress?)null }, _cancellationToken));
  }

  [Fact]
  public async Task Columns_MatchPropertiesIgnoringUnderscores() {
    var instant = Instant.FromUtc(2026, 10, 30, 12, 34, 56);
    var parameters = new { instant };
    var sql = """
      SELECT
          7                      AS foo_bar
        , @instant::timestamptz  AS baz_qux
      FROM (SELECT) AS _;
      """;

    var row = await _connection.QuerySingleAsync<Row>(sql, parameters, _cancellationToken);

    Assert.Equal(7, row.FooBar);
    Assert.Equal(instant, row.BazQux);
  }
}
