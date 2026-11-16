using JoshOlson.Shared.Data;
using Xunit;

namespace JoshOlson.Database.Baking;

// What the tests of `LayerExporter.ExportAsync` share: the tool over the fixture layers and the
// databases a test built from a layer, which are dropped when the test is done. An export is named `foo-bar` and
// described as `Foo bar` unless the test says otherwise.
internal sealed class ExportHarness : IAsyncDisposable {
  private readonly CancellationToken _cancellationToken;
  private readonly List<string> _created = [];

  private ExportHarness(SeedTool tool, CancellationToken cancellationToken) {
    Tool = tool;
    _cancellationToken = cancellationToken;
  }

  public SeedTool Tool { get; }

  public static async Task<ExportHarness> OpenAsync(CancellationToken cancellationToken)
    => new(await FixtureSeeds.Tool, cancellationToken);

  // A database built from a layer, as `seed:test` makes one, or as `seed <layer> --edit` records it.
  public async Task<string> DatabaseFromAsync(string layer, bool editing = false) {
    var name = await Tool.CreateTestDatabaseAsync(Guid.NewGuid().ToString("N"), layer, _cancellationToken);
    _created.Add(name);
    if (editing) {
      await RunAsync(name, "UPDATE seed.hydration SET editing = TRUE;");
    }
    return name;
  }

  public async Task RunAsync(string database, string sql) {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, _cancellationToken);
    await connection.ExecuteAsync(sql, _cancellationToken);
  }

  public async Task<LayerExport> ExportAsync(
    string database,
    string? name = "foo-bar",
    string? description = "Foo bar",
    bool overwrite = false
  )
    => await Tool.Exporter.ExportAsync(
      new() {
        Database = database,
        Name = name,
        Description = description,
        Overwrite = overwrite,
      },
      _cancellationToken);

  // The message of the export that is refused.
  public async Task<string> RefusalAsync(
    string database,
    string? name = "foo-bar",
    string? description = "Foo bar",
    bool overwrite = false
  ) {
    var exception = await Assert.ThrowsAsync<SeedFormatException>(
      () => ExportAsync(database, name, description, overwrite));
    return exception.Message;
  }

  public async ValueTask DisposeAsync() {
    foreach (var name in _created) {
      await SeedTestServer.Databases.DropAsync(name, CancellationToken.None);
    }
  }
}
