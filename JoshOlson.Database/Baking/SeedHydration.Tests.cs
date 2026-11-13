using NodaTime;
using Xunit;

namespace JoshOlson.Database.Baking;

// Each test writes the row in a clone of the real schema template, which has the table and no row.
public sealed class SeedHydrationTests {
  private static readonly Instant _Anchor = Instant.FromUtc(2026, 1, 2, 3, 4, 5).PlusNanoseconds(678_000);
  private static readonly Instant _Later = Instant.FromUtc(2026, 6, 7, 8, 9, 10);
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

  [Fact]
  public async Task ReadAsync_NoRow_IsTheAnchorGivenOrRefused() {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);

    var read = await SeedHydration.ReadAsync(connection, _Anchor, _cancellationToken);
    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => SeedHydration.ReadAsync(connection, anchorIfUnrecorded: null, _cancellationToken));

    Assert.Equal((_Anchor, null, false), read);
    Assert.Contains("no seed.hydration row", exception.Message, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("foo", true)]
  [InlineData("foo", false)]
  [InlineData(null, false)]
  public async Task RecordAsync_LayerOrNone_ReadsBackAsWritten(string? layer, bool editing) {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);

    await SeedHydration.RecordAsync(connection, layer, editing, _Anchor, _cancellationToken);

    Assert.Equal((_Anchor, layer, editing), await SeedHydration.ReadAsync(connection, _Later, _cancellationToken));
  }

  [Fact]
  public async Task RecordExportAsync_NoRow_RecordsTheLayerForEditingAtTheAnchor() {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);

    await SeedHydration.RecordExportAsync(connection, "foo", _Anchor, _cancellationToken);

    Assert.Equal((_Anchor, "foo", true), await SeedHydration.ReadAsync(connection, _Later, _cancellationToken));
  }

  [Fact]
  public async Task RecordExportAsync_RowThere_ChangesTheLayerAndKeepsTheAnchor() {
    await using var database = await ScratchDatabase.CreateAsync(_cancellationToken);
    await using var connection = await database.OpenAsync(_cancellationToken);
    await SeedHydration.RecordAsync(connection, "foo", editing: false, _Anchor, _cancellationToken);

    await SeedHydration.RecordExportAsync(connection, "bar", _Later, _cancellationToken);

    Assert.Equal((_Anchor, "bar", true), await SeedHydration.ReadAsync(connection, anchorIfUnrecorded: null, _cancellationToken));
  }
}
