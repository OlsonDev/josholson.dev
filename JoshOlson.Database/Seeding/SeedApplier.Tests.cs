using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Database.Seeding;

// Runs against the dev database, which must have the repository's migrations applied. Every test rolls back, so the
// dev database keeps none of the rows.
public sealed class SeedApplierTests {
  private static readonly Instant _Anchor = Instant.FromUtc(2026, 1, 1, 12, 0);
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task ApplyAsync_Chain_InsertsTheMergedRowsInForeignKeyOrder() {
    var schema = await LoadSchemaAsync();
    var tree = new LayerTree([
      LayerParser.Parse(
        "foo",
        """
        description: Base
        tables:
          tag.alias:
            insert:
              - { alias_id: 900001, tag_id: 900001, name: Quux }
          tag.tag:
            insert:
              - { tag_id: 900001, name: Corge, slug: seed-applier-corge, description: ~, color: ~ }
        """,
        schema),
      LayerParser.Parse(
        "bar",
        """
        description: Child
        extends: foo
        tables:
          tag.tag:
            update:
              - { tag_id: 900001, description: Grault }
        """,
        schema),
    ]);
    await using var connection = await OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(_cancellationToken);

    var rows = await SeedApplier.ApplyAsync(transaction, tree, schema, "bar", _Anchor, _cancellationToken);

    var description = await connection.QuerySingleAsync<string>(
      "SELECT description FROM tag.tag WHERE tag_id = 900001;",
      new { },
      _cancellationToken);
    var aliases = await connection.QuerySingleAsync<long>(
      "SELECT count(*) FROM tag.alias WHERE tag_id = 900001;",
      new { },
      _cancellationToken);
    Assert.Equal(2, rows);
    Assert.Equal("Grault", description);
    Assert.Equal(1, aliases);
  }

  [Fact]
  public async Task ApplyAsync_RowThatBreaksAConstraint_NamesTheTable() {
    var schema = await LoadSchemaAsync();
    var tree = new LayerTree([
      LayerParser.Parse(
        "foo",
        """
        description: Base
        tables:
          tag.alias:
            insert:
              - { alias_id: 900002, tag_id: 900002, name: Quux }
        """,
        schema),
    ]);
    await using var connection = await OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(_cancellationToken);

    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => SeedApplier.ApplyAsync(transaction, tree, schema, "foo", _Anchor, _cancellationToken));

    Assert.Contains("tag.alias", exception.Message, StringComparison.Ordinal);
  }

  private async Task<NpgsqlConnection> OpenAsync() {
    var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await connection.OpenAsync(_cancellationToken);
    return connection;
  }

  private async Task<SeedSchema> LoadSchemaAsync()
    => await SeedSchema.LoadAsync(SeedPaths.ForRepository(), _cancellationToken);
}
