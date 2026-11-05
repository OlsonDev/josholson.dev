using Npgsql;
using Xunit;

namespace JoshOlson.Shared.Data;

public class NpgsqlConnectionExtensionsTests : IAsyncLifetime {
  private const string _TwoRowsSql = """
    SELECT
        generate_series(1, 2)  AS n
    FROM (SELECT) AS _;
    """;

  private const string _ParameterSql = """
    SELECT
        @foo  AS value
    FROM (SELECT) AS _;
    """;

  private const string _NoRowsSql = """
    SELECT
        1  AS value
    FROM (SELECT) AS _
    WHERE FALSE;
    """;

  private const string _OneRowSql = """
    SELECT
        1  AS value
    FROM (SELECT) AS _;
    """;

  private const string _TwoResultSetsSql = """
    SELECT
        1  AS value
    FROM (SELECT) AS _;

    SELECT
        'foo'  AS value
    FROM (SELECT) AS _;
    """;

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private NpgsqlConnection _connection = null!;

  public async ValueTask InitializeAsync()
    => _connection = await TestDatabase.DataSource.OpenConnectionAsync(_cancellationToken);

  public async ValueTask DisposeAsync()
    => await _connection.DisposeAsync();

  [Fact]
  public async Task Queries_ReturnRowsWithAndWithoutParameters() {
    var parameters = new { foo = 7 };

    Assert.Equal([1, 2], await _connection.QueryAsync<int>(_TwoRowsSql, _cancellationToken));
    Assert.Equal([7], await _connection.QueryAsync<int>(_ParameterSql, parameters, _cancellationToken));
    Assert.Equal(7, await _connection.QuerySingleAsync<int>(_ParameterSql, parameters, _cancellationToken));
    Assert.Equal(7, await _connection.QuerySingleOrDefaultAsync<int?>(_ParameterSql, parameters, _cancellationToken));
    Assert.Null(await _connection.QuerySingleOrDefaultAsync<int?>(_NoRowsSql, null, _cancellationToken));
  }

  [Fact]
  public async Task QueryMultiple_ReadsEachResultSet() {
    await using var data = await _connection.QueryMultipleAsync(_TwoResultSetsSql, null, _cancellationToken);

    Assert.Equal(1, await data.ReadSingleAsync<int>());
    Assert.Equal("foo", await data.ReadSingleAsync<string>());
  }

  [Fact]
  public async Task Execute_ReturnsTheRowCount()
    => Assert.Equal(-1, await _connection.ExecuteAsync(_OneRowSql, null, _cancellationToken));

  // The reason the token is a required argument: every call must honor it.
  [Fact]
  public async Task Calls_HonorTheCancellationToken() {
    using var cancelled = new CancellationTokenSource();
    await cancelled.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => _connection.QueryAsync<int>(_OneRowSql, cancelled.Token));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => _connection.QuerySingleAsync<int>(_OneRowSql, null, cancelled.Token));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => _connection.ExecuteAsync(_OneRowSql, null, cancelled.Token));
  }
}
