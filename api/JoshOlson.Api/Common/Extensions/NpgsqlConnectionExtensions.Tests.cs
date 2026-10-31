using JoshOlson.Api.Common.Testing;
using Xunit;

namespace JoshOlson.Api.Common.Extensions;

public class NpgsqlConnectionExtensionsTests {
  [Fact]
  public async Task Queries_ReturnRowsWithAndWithoutParameters() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var parameters = new { foo = 7 };

    Assert.Equal([1, 2], await connection.QueryAsync<int>("SELECT * FROM (VALUES (1), (2)) AS _;", cancellationToken));
    Assert.Equal([7], await connection.QueryAsync<int>("SELECT @foo;", parameters, cancellationToken));
    Assert.Equal(7, await connection.QuerySingleAsync<int>("SELECT @foo;", parameters, cancellationToken));
    Assert.Equal(7, await connection.QuerySingleOrDefaultAsync<int?>("SELECT @foo;", parameters, cancellationToken));
    Assert.Null(await connection.QuerySingleOrDefaultAsync<int?>("SELECT 1 WHERE FALSE;", null, cancellationToken));
  }

  [Fact]
  public async Task QueryMultiple_ReadsEachResultSet() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);

    await using var data = await connection.QueryMultipleAsync("SELECT 1; SELECT 'foo';", null, cancellationToken);

    Assert.Equal(1, await data.ReadSingleAsync<int>());
    Assert.Equal("foo", await data.ReadSingleAsync<string>());
  }

  [Fact]
  public async Task Execute_ReturnsTheRowCount() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);

    Assert.Equal(-1, await connection.ExecuteAsync("SELECT 1;", null, cancellationToken));
  }

  // The reason the token is a required argument: every call must honor it.
  [Fact]
  public async Task Calls_HonorTheCancellationToken() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    using var cancelled = new CancellationTokenSource();
    await cancelled.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => connection.QueryAsync<int>("SELECT 1;", cancelled.Token));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => connection.QuerySingleAsync<int>("SELECT 1;", null, cancelled.Token));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => connection.ExecuteAsync("SELECT 1;", null, cancelled.Token));
  }
}
