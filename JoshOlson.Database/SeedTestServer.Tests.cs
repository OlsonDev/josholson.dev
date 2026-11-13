using JoshOlson.Database.Baking;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database;

// What every test of the seed tool that reaches the server would otherwise build for itself, made once per test run:
// the real repository's paths, lock, schema template, and tool, the database operations, the container tools, and a
// connection to the maintenance database. All of it is safe to share between tests that run side by side: none of these types keeps state a
// test changes (`ComposeTools` remembers which container is which, in a concurrent dictionary, so sharing one saves
// every test after the first the lookup).
public static class SeedTestServer {
  public static ServerDatabases Databases { get; } = new();

  // Always over the real repository root, even for a test that works on a copy: it needs the compose files, not the
  // seeds.
  public static ComposeTools Tools { get; } = new(SeedPaths.ForRepository().Root);

  private static readonly Lazy<SeedSchema> _Schema = new(
    () => new(SchemaLockParser.Parse(File.ReadAllText(SeedPaths.ForRepository().LockFile))));

  // Opened with no token of a test's, so a test that is canceled does not leave a canceled task for the others.
  private static readonly Lazy<Task<SeedTool>> _RealTool = new(
    () => SeedTool.OpenAsync(Paths, Databases, Tools, Schema, Clock, CancellationToken.None));

  public static SeedPaths Paths
    => SeedPaths.ForRepository();

  public static IClock Clock
    => SystemClock.Instance;

  // The real lock as the layer parser reads it, parsed once.
  public static SeedSchema Schema
    => _Schema.Value;

  // The schema template for the real migrations, which is on the server after the first test that asks for it.
  public static SchemaTemplate RealTemplate { get; } = new(
    SchemaTemplateOptions.ForRepository(SeedPaths.ForRepository()),
    Tools,
    Databases);

  // The tool over the real repository (the real layers). `SeedTool` only reads what it was opened with, and
  // `WithDevelopment` returns another tool, so one serves every test.
  public static Task<SeedTool> RealTool
    => _RealTool.Value;

  // An owner connection to the maintenance database `postgres`, where a test that drops or clones databases connects.
  // The caller disposes it. For the dev database or one a test made, call `DatabaseEnvironment.OpenOwnerAsync`.
  public static async Task<NpgsqlConnection> OpenMaintenanceAsync(CancellationToken cancellationToken)
    => await DatabaseEnvironment.OpenOwnerAsync("postgres", cancellationToken);
}
