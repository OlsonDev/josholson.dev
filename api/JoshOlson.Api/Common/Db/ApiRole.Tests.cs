using JoshOlson.Api.Common.Testing;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Common.Db;

// The API connects as a role that can change rows and nothing else (ADR 0018). Each statement runs in a transaction
// that is never committed, so if a grant were too wide the statement would still be undone.
public class ApiRoleTests {
  [Fact]
  public async Task TestDatabase_ConnectsAsTheApiRole() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    await using var command = new NpgsqlCommand("SELECT current_user::text;", connection);

    Assert.Equal("josholson_api", await command.ExecuteScalarAsync(cancellationToken));
  }

  [Theory]
  [InlineData("CREATE TABLE tag.foo (bar INT)")]
  [InlineData("CREATE TABLE public.foo (bar INT)")]
  [InlineData("CREATE SCHEMA foo")]
  [InlineData("ALTER TABLE tag.tag ADD COLUMN foo INT")]
  [InlineData("DROP TABLE tag.tag")]
  [InlineData("TRUNCATE tag.tag")]
  public async Task Structure_CannotBeChanged(string statement) {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
    await using var command = new NpgsqlCommand(statement, connection, transaction);

    var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(cancellationToken));

    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
  }

  [Fact]
  public async Task Rows_CanBeInsertedUpdatedAndDeleted() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
    await connection.OpenAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
    var slug = $"test-{Guid.NewGuid():N}";

    await using var insert = new NpgsqlCommand(
      "INSERT INTO tag.tag (name, slug) VALUES ($1, $1) RETURNING tag_id;", connection, transaction) {
      Parameters = { new() { Value = slug } },
    };
    var tagId = (int)(await insert.ExecuteScalarAsync(cancellationToken))!;
    await using var update = new NpgsqlCommand(
      "UPDATE tag.tag SET description = 'foo' WHERE tag_id = $1;", connection, transaction) {
      Parameters = { new() { Value = tagId } },
    };
    await using var delete = new NpgsqlCommand(
      "DELETE FROM tag.tag WHERE tag_id = $1;", connection, transaction) {
      Parameters = { new() { Value = tagId } },
    };

    Assert.Equal(1, await update.ExecuteNonQueryAsync(cancellationToken));
    Assert.Equal(1, await delete.ExecuteNonQueryAsync(cancellationToken));
  }
}
