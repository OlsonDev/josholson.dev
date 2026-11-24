using JoshOlson.Api.Common.Testing;
using JoshOlson.Shared.Data;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Common.Db;

public sealed class ScopedDatabaseRegistryTests : IAsyncDisposable {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ScopedDatabaseTestData _scopedDatabaseTestData;
  private readonly ScopedDatabaseRegistry _registry
    = new(TestDatabase.DataSource, TestDatabase.ConnectionString);

  public ScopedDatabaseRegistryTests(ScopedDatabaseTestData scopedDatabaseTestData)
    => _scopedDatabaseTestData = scopedDatabaseTestData;

  public ValueTask DisposeAsync()
    => _registry.DisposeAsync();

  private static async Task<string> CurrentDatabaseAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken) {
    await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
    return await connection.QuerySingleAsync<string>("SELECT current_database();", cancellationToken);
  }

  [Fact]
  public void DatabaseName_Id_IsTheSeedToolsTestPrefixAndTheId()
    => Assert.Equal("seed_test_foo42", ScopedDatabaseRegistry.DatabaseName("foo42"));

  [Fact]
  public async Task FindAsync_ExistingDatabase_ReturnsADataSourceForIt() {
    var id = await _scopedDatabaseTestData.CreateAsync(_cancellationToken);

    var dataSource = await _registry.FindAsync(id, _cancellationToken);

    Assert.NotNull(dataSource);
    Assert.Equal(ScopedDatabaseRegistry.DatabaseName(id), await CurrentDatabaseAsync(dataSource, _cancellationToken));
  }

  [Fact]
  public async Task FindAsync_SameIdTwice_ReturnsTheSameDataSource() {
    var id = await _scopedDatabaseTestData.CreateAsync(_cancellationToken);

    var first = await _registry.FindAsync(id, _cancellationToken);
    var second = await _registry.FindAsync(id, _cancellationToken);

    Assert.Same(first, second);
  }

  [Fact]
  public async Task FindAsync_IdWithNoDatabase_ReturnsNull()
    => Assert.Null(await _registry.FindAsync("nosuchscope", _cancellationToken));

  [Theory]
  [InlineData("")]
  [InlineData("Foo")]
  [InlineData("foo-bar")]
  [InlineData("foo_bar")]
  [InlineData("foo bar")]
  [InlineData("foo\"; DROP DATABASE josholson; --")]
  [InlineData("../foo")]
  [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
  public async Task FindAsync_IdThatIsNotLowercaseLettersAndDigits_ReturnsNull(string id)
    => Assert.Null(await _registry.FindAsync(id, _cancellationToken));

  [Fact]
  public async Task FindAsync_DatabaseMadeAgainUnderTheSameId_ReturnsANewDataSourceThatWorks() {
    var id = await _scopedDatabaseTestData.CreateAsync(_cancellationToken);
    var first = await _registry.FindAsync(id, _cancellationToken);
    Assert.NotNull(first);
    _ = await CurrentDatabaseAsync(first, _cancellationToken);

    await _scopedDatabaseTestData.RecreateAsync(id, _cancellationToken);
    var second = await _registry.FindAsync(id, _cancellationToken);

    Assert.NotNull(second);
    Assert.NotSame(first, second);
    Assert.Equal(ScopedDatabaseRegistry.DatabaseName(id), await CurrentDatabaseAsync(second, _cancellationToken));
  }

  [Fact]
  public async Task FindAsync_DatabaseDroppedAfterwards_ReturnsNull() {
    var id = await _scopedDatabaseTestData.CreateAsync(_cancellationToken);
    Assert.NotNull(await _registry.FindAsync(id, _cancellationToken));

    await _scopedDatabaseTestData.DropAsync(id, _cancellationToken);

    Assert.Null(await _registry.FindAsync(id, _cancellationToken));
  }
}
