using System.Net;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Shared.Data;

public class DapperConfigurationTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private NpgsqlConnection _connection = null!;

  private sealed record Row {
    public required int FooBar { get; init; }
    public required Instant? BazQux { get; init; }
  }

  private sealed record ListRow {
    public required List<string> Names { get; init; }
  }

  public async ValueTask InitializeAsync()
    => _connection = await TestDatabase.DataSource.OpenConnectionAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _connection.DisposeAsync();

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

  [Fact]
  public async Task StringList_RoundTripsAsParameterAndResult() {
    List<string> names = [
      "foo",
      "bar baz",
      "qux,quux",
    ];
    var sql = """
      SELECT
          @names::text[]  AS names
      FROM (SELECT) AS _;
      """;

    Assert.Equal(names, await _connection.QuerySingleAsync<List<string>>(sql, new { names }, _cancellationToken));
    Assert.Equal(names, (await _connection.QuerySingleAsync<ListRow>(sql, new { names }, _cancellationToken)).Names);
  }

  [Fact]
  public async Task StringList_Empty_StaysAnEmptyList() {
    var sql = """
      SELECT
          @names::text[]  AS names
      FROM (SELECT) AS _;
      """;

    Assert.Empty(await _connection.QuerySingleAsync<List<string>>(sql, new { names = new List<string>() }, _cancellationToken));
  }

  [Fact]
  public async Task StringArray_ParameterWorksWithAny() {
    string[] names = [
      "foo",
      "bar",
    ];
    var sql = """
      SELECT
          n  AS name
      FROM (SELECT) AS _
      JOIN unnest(ARRAY['foo', 'baz', 'bar']) AS u (n)  ON n = ANY (@names)
      ORDER BY n;
      """;

    Assert.Equal(["bar", "foo"], await _connection.QueryAsync<string>(sql, new { names }, _cancellationToken));
  }
}
