using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Shared.Data;
using Xunit;

namespace JoshOlson.Seed.Commands;

public sealed class TestCommandTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task Execute_IdAndLayerAndThenTheIdAgain_MakesTheDatabaseOnceAndRefusesTheSecond() {
    using var workspace = await FixtureSeeds.WorkspaceAsync(_cancellationToken);
    var id = Guid.NewGuid().ToString("N");
    var database = SeedDatabaseNames.Test(id);
    try {
      var (exitCode, output) = await SeedAppRunner.RunAsync(workspace, "test", id, FixtureSeeds.Child);
      var (againCode, again) = await SeedAppRunner.RunAsync(workspace, "test", id, FixtureSeeds.Child);

      Assert.True(exitCode is 0, output);
      Assert.Contains(database, output, StringComparison.Ordinal);
      await using var clone = await DatabaseEnvironment.OpenOwnerAsync(database, _cancellationToken);
      var tags = await clone.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken);
      Assert.Equal(FixtureSeeds.ChildRows, tags);
      Assert.Equal(1, againCode);
      Assert.Contains("already exists", again, StringComparison.Ordinal);
    } finally {
      await using var owner = await SeedTestServer.OpenMaintenanceAsync(CancellationToken.None);
      await SeedTestServer.Databases.DropAsync(owner, database, CancellationToken.None);
    }
  }

  // Refused before any database is made, so there is nothing to drop.
  [Fact]
  public async Task Execute_IdThatIsNotLowercaseLettersAndDigits_FailsWithExitOne() {
    var (exitCode, output) = await SeedAppRunner.RunAsync("test", "Foo-Bar", "foo");

    Assert.Equal(1, exitCode);
    Assert.Contains("not a test id", output, StringComparison.Ordinal);
  }
}
