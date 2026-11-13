using JoshOlson.Shared.IO;
using Xunit;

namespace JoshOlson.Database.Layers;

public sealed class SeedSchemaTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task LoadAsync_RepositoryLock_HasItsTablesInInsertOrder() {
    var schema = await SeedSchema.LoadAsync(SeedPaths.ForRepository(), _cancellationToken);

    Assert.NotEmpty(schema.InsertOrder());
    Assert.All(schema.InsertOrder(), table => Assert.Same(table, schema.Find(table.Table.Name)));
  }

  [Fact]
  public async Task LoadAsync_NoLock_SaysToRunSeedsMigrate() {
    var directory = TempDirectory.Create("seed-schema");
    try {
      var paths = new SeedPaths(directory);

      var exception = await Assert.ThrowsAsync<SeedFormatException>(() => SeedSchema.LoadAsync(paths, _cancellationToken));

      Assert.Equal($"{paths.LockFile} does not exist. Run mise run seeds:migrate first.", exception.Message);
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }
}
