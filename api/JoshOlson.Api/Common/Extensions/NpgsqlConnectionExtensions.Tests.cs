using JoshOlson.Api.Common.Testing;
using Xunit;

namespace JoshOlson.Api.Common.Extensions;

public class NpgsqlConnectionExtensionsTests {
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

  [Fact]
  public async Task Queries_ReturnRowsWithAndWithoutParameters() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var parameters = new { foo = 7 };

    Assert.Equal([1, 2], await connection.QueryAsync<int>(_TwoRowsSql, cancellationToken));
    Assert.Equal([7], await connection.QueryAsync<int>(_ParameterSql, parameters, cancellationToken));
    Assert.Equal(7, await connection.QuerySingleAsync<int>(_ParameterSql, parameters, cancellationToken));
    Assert.Equal(7, await connection.QuerySingleOrDefaultAsync<int?>(_ParameterSql, parameters, cancellationToken));
    Assert.Null(await connection.QuerySingleOrDefaultAsync<int?>(_NoRowsSql, null, cancellationToken));
  }

  [Fact]
  public async Task QueryMultiple_ReadsEachResultSet() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);

    await using var data = await connection.QueryMultipleAsync(_TwoResultSetsSql, null, cancellationToken);

    Assert.Equal(1, await data.ReadSingleAsync<int>());
    Assert.Equal("foo", await data.ReadSingleAsync<string>());
  }

  [Fact]
  public async Task Execute_ReturnsTheRowCount() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);

    Assert.Equal(-1, await connection.ExecuteAsync(_OneRowSql, null, cancellationToken));
  }

  // The reason the token is a required argument: every call must honor it.
  [Fact]
  public async Task Calls_HonorTheCancellationToken() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    using var cancelled = new CancellationTokenSource();
    await cancelled.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => connection.QueryAsync<int>(_OneRowSql, cancelled.Token));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => connection.QuerySingleAsync<int>(_OneRowSql, null, cancelled.Token));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => connection.ExecuteAsync(_OneRowSql, null, cancelled.Token));
  }
}
