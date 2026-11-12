using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Migrate;
using NodaTime;

namespace JoshOlson.Seed;

// Opens the `SeedTool` for a run. The tool needs the layer tree and the schema, which are read from disk (or, for
// `seeds:migrate`, built in memory), so it cannot be a registration of its own; what it is built from can, and this
// hands it those, so a test can swap the clock, the paths, or the dev database (the one `seed` drops and makes again,
// which a test must point at a database of its own).
public sealed class SeedToolFactory {
  private readonly SeedPaths _paths;
  private readonly ComposeTools _tools;
  private readonly string _development;
  private readonly ISnapshotSource? _snapshots;

  // `snapshots` is where `seeds:migrate` finds the base; a test that does not care leaves it, and a test that does hands
  // it one of its own. Without one it is git (`GitSnapshotSource`).
  public SeedToolFactory(
    SeedPaths paths,
    ServerDatabases databases,
    ComposeTools tools,
    IClock clock,
    string development,
    ISnapshotSource? snapshots = null
  ) {
    _paths = paths;
    Databases = databases;
    _tools = tools;
    Clock = clock;
    _development = development;
    _snapshots = snapshots;
  }

  public ISnapshotSource OpenSnapshotSource()
    => _snapshots ?? new GitSnapshotSource(_paths);

  public ServerDatabases Databases { get; }

  public IClock Clock { get; }

  // The schema template for the repository's migrations, which `seeds:migrate` reads the schema from.
  public SchemaTemplate OpenSchemaTemplate()
    => new(SchemaTemplateOptions.ForRepository(_paths), _tools, Databases);

  // Reads the lock under the repository root. It has to exist: it types every value in a layer.
  public async Task<SeedTool> OpenAsync(CancellationToken cancellationToken)
    => (await SeedTool.OpenAsync(_paths, Databases, _tools, Clock, cancellationToken)).WithDevelopment(_development);

  // For a schema the caller already has, as `seeds:migrate` has before the lock is written.
  public async Task<SeedTool> OpenAsync(SeedSchema schema, CancellationToken cancellationToken)
    => (await SeedTool.OpenAsync(_paths, Databases, _tools, schema, Clock, cancellationToken)).WithDevelopment(_development);
}
