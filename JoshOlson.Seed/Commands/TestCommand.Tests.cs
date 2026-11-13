using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Data;
using Xunit;

namespace JoshOlson.Seed.Commands;

public sealed class TestCommandTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly string _id = Guid.NewGuid().ToString("N");
  private readonly ServerDatabases _databases = SeedTestServer.Databases;

  public ValueTask InitializeAsync()
    => ValueTask.CompletedTask;

  public async ValueTask DisposeAsync() {
    await using var owner = await SeedTestServer.OpenMaintenanceAsync(CancellationToken.None);
    await _databases.DropAsync(owner, SeedDatabaseNames.Test(_id), CancellationToken.None);
  }

  [Fact]
  public async Task Execute_IdAndLayer_MakesTheDatabaseAndPrintsItsName() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "test", _id, SeedLayers.MoreTags);

    Assert.True(exitCode is 0, output);
    Assert.Contains(SeedDatabaseNames.Test(_id), output, StringComparison.Ordinal);
    await using var clone = await DatabaseEnvironment.OpenOwnerAsync(SeedDatabaseNames.Test(_id), _cancellationToken);
    Assert.Equal(22, await clone.QuerySingleAsync<long>("SELECT count(*) FROM tag.tag;", _cancellationToken));
  }

  [Fact]
  public async Task Execute_IdInUse_FailsWithTheMessageAndExitOne() {
    _ = await SeedAppRunner.RunAsync(paths: null, "test", _id, SeedLayers.Minimal);

    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "test", _id, SeedLayers.Minimal);

    Assert.Equal(1, exitCode);
    Assert.Contains("already exists", output, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Execute_IdThatIsNotLowercaseLettersAndDigits_FailsWithExitOne() {
    var (exitCode, output) = await SeedAppRunner.RunAsync(paths: null, "test", "Foo-Bar", SeedLayers.Minimal);

    Assert.Equal(1, exitCode);
    Assert.Contains("not a test id", output, StringComparison.Ordinal);
  }
}
