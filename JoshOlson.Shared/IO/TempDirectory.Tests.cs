using Xunit;

namespace JoshOlson.Shared.IO;

public sealed class TempDirectoryTests : IDisposable {
  private readonly string _directory = TempDirectory.Create("temp-directory-tests");

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  [Fact]
  public void Create_MakesANamedDirectoryUnderTheRepositorysTempDirectory() {
    Assert.True(Directory.Exists(_directory));
    Assert.Equal(Path.Combine(RepositoryRoot.Find(), TempDirectory.Name), Path.GetDirectoryName(_directory));
    Assert.StartsWith("temp-directory-tests-", Path.GetFileName(_directory), StringComparison.Ordinal);
  }

  [Fact]
  public void Sweep_RemovesOnlyDirectoriesOlderThanTheLimit() {
    var old = Path.Combine(_directory, "old");
    var recent = Path.Combine(_directory, "recent");
    _ = Directory.CreateDirectory(old);
    File.WriteAllText(Path.Combine(old, "foo.txt"), "foo");
    Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-7));
    _ = Directory.CreateDirectory(recent);

    var swept = TempDirectory.Sweep(_directory, TempDirectory.MaxAge);

    Assert.Equal(1, swept);
    Assert.False(Directory.Exists(old));
    Assert.True(Directory.Exists(recent));
  }
}
