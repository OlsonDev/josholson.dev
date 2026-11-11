using Xunit;

namespace JoshOlson.Database;

public sealed class SeedPathsTests {
  [Fact]
  public void ForRepository_EveryPathExists() {
    var paths = SeedPaths.ForRepository();

    Assert.True(Directory.Exists(paths.Migrations));
    Assert.True(Directory.Exists(paths.Seeds));
    Assert.True(File.Exists(paths.LockFile));
    Assert.True(File.Exists(paths.ProvisioningScript));
    Assert.True(File.Exists(paths.SeedSchemaScript));
  }

  [Fact]
  public void ForRepository_FoundOnce_SameInstanceEveryTime()
    => Assert.Same(SeedPaths.ForRepository(), SeedPaths.ForRepository());

  [Fact]
  public void LockFile_IsBesideTheMigrations()
    => Assert.Equal(Path.Combine("/foo", "JoshOlson.Database", "migrations", "schema.lock.yaml"), new SeedPaths("/foo").LockFile);
}
