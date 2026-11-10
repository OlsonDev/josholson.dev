using Xunit;

namespace JoshOlson.Shared.IO;

public sealed class RepositoryRootTests : IDisposable {
  private readonly string _directory = Directory.CreateTempSubdirectory("repository-root-").FullName;

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  [Fact]
  public void TryFind_BelowTheSolution_ReturnsTheSolutionDirectory() {
    File.WriteAllText(Path.Combine(_directory, "JoshOlson.slnx"), "");
    var nested = Directory.CreateDirectory(Path.Combine(_directory, "foo", "bar")).FullName;

    Assert.Equal(_directory, RepositoryRoot.TryFind(nested));
  }

  [Fact]
  public void TryFind_NoSolutionAbove_ReturnsNull()
    => Assert.Null(RepositoryRoot.TryFind(_directory));

  [Fact]
  public void Find_FromTheTestOutput_FindsTheRealRepository()
    => Assert.True(File.Exists(Path.Combine(RepositoryRoot.Find(), "JoshOlson.slnx")));
}
