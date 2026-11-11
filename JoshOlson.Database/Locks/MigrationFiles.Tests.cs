using System.Text;
using JoshOlson.Shared.IO;
using Xunit;

namespace JoshOlson.Database.Locks;

public sealed class MigrationFilesTests : IDisposable {
  private readonly string _directory = TempDirectory.Create("migrations");
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  public void Dispose()
    => Directory.Delete(_directory, recursive: true);

  private Task WriteAsync(string file, string content)
    => File.WriteAllBytesAsync(Path.Combine(_directory, file), Encoding.UTF8.GetBytes(content), _cancellationToken);

  [Theory]
  [InlineData("V1__foo.sql", true, 1)]
  [InlineData("V042__foo_bar.sql", true, 42)]
  [InlineData("V0__foo.sql", true, 0)]
  [InlineData("V12__a.sql", true, 12)]
  [InlineData("R__foo.sql", false, 0)]
  [InlineData("V__foo.sql", false, 0)]
  [InlineData("V1_foo.sql", false, 0)]
  [InlineData("V1__.sql", false, 0)]
  [InlineData("V1__foo.txt", false, 0)]
  [InlineData("v1__foo.sql", false, 0)]
  [InlineData("V1.2__foo.sql", false, 0)]
  [InlineData("V-1__foo.sql", false, 0)]
  [InlineData("Vx__foo.sql", false, 0)]
  [InlineData("schema.lock.yaml", false, 0)]
  public void TryParseVersion_ReadsOnlyVersionedMigrationNames(string file, bool expected, int version) {
    Assert.Equal(expected, MigrationFiles.TryParseVersion(file, out var actual));
    Assert.Equal(version, actual);
  }

  [Fact]
  public async Task LoadAsync_ReturnsVersionedMigrationsByNumber_AndSkipsEverythingElse() {
    await WriteAsync("V010__ten.sql", "ten");
    await WriteAsync("V002__two.sql", "two");
    await WriteAsync("V001__one.sql", "one");
    await WriteAsync("R__reference.sql", "reference");
    await WriteAsync("schema.lock.yaml", "version: 10");
    await WriteAsync("notes.md", "notes");

    var migrations = await MigrationFiles.LoadAsync(_directory, _cancellationToken);

    Assert.Equal(["V001__one.sql", "V002__two.sql", "V010__ten.sql"], migrations.Select(migration => migration.File));
    Assert.Equal([1, 2, 10], migrations.Select(migration => migration.Version));
  }

  [Fact]
  public async Task LoadAsync_ChecksumIsTheSha256OfTheBytes() {
    await WriteAsync("V1__abc.sql", "abc");

    var migrations = await MigrationFiles.LoadAsync(_directory, _cancellationToken);

    Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", migrations[0].Checksum);
  }

  [Theory]
  [InlineData("SELECT 1;\n", "SELECT 1; \n")]
  [InlineData("SELECT 1;\n", "SELECT 1;\n\n")]
  [InlineData("SELECT 1;\n", "-- note\nSELECT 1;\n")]
  [InlineData("SELECT 'a b';\n", "SELECT 'a  b';\n")]
  public async Task LoadAsync_AnyByteChange_ChangesTheChecksum(string before, string after) {
    await WriteAsync("V1__foo.sql", before);
    var original = (await MigrationFiles.LoadAsync(_directory, _cancellationToken))[0].Checksum;

    await WriteAsync("V1__foo.sql", after);
    var edited = (await MigrationFiles.LoadAsync(_directory, _cancellationToken))[0].Checksum;

    Assert.NotEqual(original, edited);
  }

  [Fact]
  public async Task LoadAsync_TwoFilesWithOneVersion_ThrowsNamingBoth() {
    await WriteAsync("V043__from_main.sql", "a");
    await WriteAsync("V043__from_my_branch.sql", "b");

    var exception = await Assert.ThrowsAsync<SeedFormatException>(() => MigrationFiles.LoadAsync(_directory, _cancellationToken));

    Assert.Contains("V043__from_main.sql", exception.Message, StringComparison.Ordinal);
    Assert.Contains("V043__from_my_branch.sql", exception.Message, StringComparison.Ordinal);
    Assert.Contains("rename yours", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task LoadAsync_EmptyDirectory_ReturnsNothing()
    => Assert.Empty(await MigrationFiles.LoadAsync(_directory, _cancellationToken));
}
