using JoshOlson.Database.Layers;
using Npgsql;

namespace JoshOlson.Database.Baking;

// A database of a test's own: a `ScratchClone` of the real schema template (built the first time, then reused by every
// test that asks) with the owner connection that made it, both gone when the test is done. Tests that put rows into one
// can commit, and leave nothing behind in the dev database.
internal sealed class ScratchDatabase : IAsyncDisposable {
  private readonly NpgsqlConnection _owner;
  private readonly ScratchClone _clone;

  private ScratchDatabase(NpgsqlConnection owner, ScratchClone clone) {
    _owner = owner;
    _clone = clone;
  }

  public string Name
    => _clone.Name;

  public static async Task<ScratchDatabase> CreateAsync(CancellationToken cancellationToken) {
    var paths = SeedPaths.ForRepository();
    var databases = new ServerDatabases();
    var template = new SchemaTemplate(SchemaTemplateOptions.ForRepository(paths), new(paths.Root), databases);
    var owner = await DatabaseEnvironment.OpenOwnerAsync(cancellationToken);
    try {
      var (templateName, _) = await template.EnsureAsync(owner, cancellationToken);
      return new(owner, await databases.CloneScratchAsync(owner, templateName, "scratch", cancellationToken));
    } catch {
      await owner.DisposeAsync();
      throw;
    }
  }

  // The repository's lock as the layer parser reads it.
  public static async Task<SeedSchema> LoadSchemaAsync(CancellationToken cancellationToken)
    => await SeedSchema.LoadAsync(SeedPaths.ForRepository(), cancellationToken);

  public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    => await _clone.OpenAsync(cancellationToken);

  public async ValueTask DisposeAsync() {
    await _clone.DisposeAsync();
    await _owner.DisposeAsync();
  }
}
