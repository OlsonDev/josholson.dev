using Npgsql;
using Xunit;

namespace JoshOlson.Api.Common.Db;

// The API connects as a role that can change rows and nothing else (ADR 0018). Each statement runs in a transaction
// that is never committed, so if a grant were too wide the statement would still be undone.
public class ApiRoleTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly NpgsqlConnection _connection;

  public ApiRoleTests(NpgsqlConnection connection)
    => _connection = connection;

  [Fact]
  public async Task TestDatabase_ConnectsAsTheApiRole() {
    var sql = """
      SELECT
          current_user::text  AS user_name
      FROM (SELECT) AS _;
      """;
    await using var command = new NpgsqlCommand(sql, _connection);

    Assert.Equal("josholson_api", await command.ExecuteScalarAsync(_cancellationToken));
  }

  public static TheoryData<string> StructureStatements
    => [
      """
      CREATE TABLE tag.foo (
          bar  INT  NULL
      );
      """,
      """
      CREATE TABLE public.foo (
          bar  INT  NULL
      );
      """,
      "CREATE SCHEMA foo;",
      "ALTER TABLE tag.tag ADD COLUMN foo INT NULL;",
      "DROP TABLE tag.tag;",
      "TRUNCATE tag.tag;",
    ];

  [Theory]
  [MemberData(nameof(StructureStatements))]
  public async Task Structure_CannotBeChanged(string statement) {
    await using var transaction = await _connection.BeginTransactionAsync(_cancellationToken);
    await using var command = new NpgsqlCommand(statement, _connection, transaction);

    var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(_cancellationToken));

    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
  }

  [Fact]
  public async Task Rows_CanBeInsertedUpdatedAndDeleted() {
    await using var transaction = await _connection.BeginTransactionAsync(_cancellationToken);
    var slug = $"test-{Guid.NewGuid():N}";

    var insertSql = """
      INSERT INTO tag.tag (
          name
        , slug
      ) VALUES (
          $1
        , $1
      )
      RETURNING tag_id;
      """;
    await using var insert = new NpgsqlCommand(insertSql, _connection, transaction) {
      Parameters = { new() { Value = slug } },
    };
    var tagId = (int)(await insert.ExecuteScalarAsync(_cancellationToken))!;
    var updateSql = """
      UPDATE tag.tag AS t SET
          description  = 'foo'
      WHERE t.tag_id = $1;
      """;
    await using var update = new NpgsqlCommand(updateSql, _connection, transaction) {
      Parameters = { new() { Value = tagId } },
    };
    var deleteSql = """
      DELETE FROM tag.tag AS t
      WHERE t.tag_id = $1;
      """;
    await using var delete = new NpgsqlCommand(deleteSql, _connection, transaction) {
      Parameters = { new() { Value = tagId } },
    };

    Assert.Equal(1, await update.ExecuteNonQueryAsync(_cancellationToken));
    Assert.Equal(1, await delete.ExecuteNonQueryAsync(_cancellationToken));
  }
}
