using JoshOlson.Shared.Data;
using Xunit;

namespace JoshOlson.Database.Baking;

// Clones a database named `seed_scratchclone_*` that the test makes and drops; nothing touches the dev database itself.
public sealed class ScratchCloneTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task OpenAsync_CloneOfASource_IsNamedForItsPurposeHoldsItsRowsAndIsDroppedOnDisposal() {
    var databases = SeedTestServer.Databases;
    var source = $"seed_scratchclone_{Guid.NewGuid():N}";
    await using var owner = await DatabaseEnvironment.OpenOwnerAsync(_cancellationToken);
    await databases.CreateAsync(owner, source, _cancellationToken);
    try {
      await using (var connection = await DatabaseEnvironment.OpenOwnerAsync(source, _cancellationToken)) {
        await connection.ExecuteAsync("CREATE TABLE foo (bar INT); INSERT INTO foo (bar) VALUES (42);", _cancellationToken);
      }

      var scratch = await databases.CloneScratchAsync(owner, source, "foo", _cancellationToken);
      int bar;
      string connectedTo;
      await using (var connection = await scratch.OpenAsync(_cancellationToken)) {
        bar = await connection.QuerySingleAsync<int>("SELECT bar FROM foo;", _cancellationToken);
        connectedTo = await connection.QuerySingleAsync<string>("SELECT current_database();", _cancellationToken);
      }
      var existed = await databases.ExistsAsync(owner, scratch.Name, _cancellationToken);
      await scratch.DisposeAsync();

      Assert.StartsWith("seed_foo_", scratch.Name, StringComparison.Ordinal);
      Assert.Equal(42, bar);
      Assert.Equal(scratch.Name, connectedTo);
      Assert.True(existed);
      Assert.False(await databases.ExistsAsync(owner, scratch.Name, _cancellationToken));
    } finally {
      await databases.DropAsync(owner, source, CancellationToken.None);
    }
  }
}
