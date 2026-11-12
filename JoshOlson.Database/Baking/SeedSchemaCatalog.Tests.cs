using JoshOlson.Database.Catalog;
using Xunit;

namespace JoshOlson.Database.Baking;

// A database the seed tool builds has the `seed` schema in it, which belongs to no migration, so the lock and the docs
// generated from a catalog must never list it: they would otherwise change depending on whether `seed` had rebuilt the
// dev database.
public sealed class SeedSchemaCatalogTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task ReadAsync_DatabaseWithTheSeedSchema_LeavesItOut() {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);

    var schema = await SchemaCatalog.ReadAsync(connection, new(), _cancellationToken);

    Assert.NotEmpty(schema.Tables);
    Assert.DoesNotContain(schema.Tables, table => table.Name.StartsWith("seed.", StringComparison.Ordinal));
  }
}
