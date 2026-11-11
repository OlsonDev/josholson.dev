using Xunit;

namespace JoshOlson.Shared.IO;

public sealed class SourceFilesTests : IDisposable {
  private readonly string _directory = TempDirectory.Create("source-files");
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private async Task WriteAsync(string relativePath) {
    var path = Path.Combine(_directory, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, "", _cancellationToken);
  }

  [Fact]
  public async Task Enumerate_ReturnsMatchesInOrderAndSkipsBuildOutput() {
    await WriteAsync("b.cs");
    await WriteAsync("a/c.cs");
    await WriteAsync("a/bin/d.cs");
    await WriteAsync("obj/e.cs");
    await WriteAsync("a/obj/Debug/f.cs");
    await WriteAsync("g.md");

    var files = SourceFiles.Enumerate([_directory], "*.cs");

    Assert.Equal(
      [Path.Combine(_directory, "a", "c.cs"), Path.Combine(_directory, "b.cs")],
      files);
  }

  [Fact]
  public void Enumerate_MissingDirectory_Throws()
    => Assert.Throws<DirectoryNotFoundException>(() => SourceFiles.Enumerate([Path.Combine(_directory, "nope")], "*.cs"));
}
