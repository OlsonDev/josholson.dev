using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using NodaTime;

namespace JoshOlson.Seed;

// Opens the `SeedTool` for a run. The tool needs the layer tree and the schema, which are read from disk (or, for
// `seeds:migrate`, built in memory), so it cannot be a registration of its own; what it is built from can, and this
// hands it those, so a test can swap the clock or the paths.
public sealed class SeedToolFactory {
  private readonly SeedPaths _paths;
  private readonly ServerDatabases _databases;
  private readonly ComposeTools _tools;
  private readonly IClock _clock;

  public SeedToolFactory(SeedPaths paths, ServerDatabases databases, ComposeTools tools, IClock clock) {
    _paths = paths;
    _databases = databases;
    _tools = tools;
    _clock = clock;
  }

  // Reads the lock under the repository root. It has to exist: it types every value in a layer.
  public async Task<SeedTool> OpenAsync(CancellationToken cancellationToken)
    => await SeedTool.OpenAsync(_paths, _databases, _tools, _clock, cancellationToken);

  // For a schema the caller already has, as `seeds:migrate` has before the lock is written.
  public async Task<SeedTool> OpenAsync(SeedSchema schema, CancellationToken cancellationToken)
    => await SeedTool.OpenAsync(_paths, _databases, _tools, schema, _clock, cancellationToken);
}
