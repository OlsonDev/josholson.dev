using JoshOlson.Database;
using JoshOlson.Database.Migrate;
using JoshOlson.Seed.Composition;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace JoshOlson.Seed;

// Runs the real command line, wired as `Program` wires it, with the console swapped for one that keeps its output and
// the factory for one over what the tests share. The paths are the repository's unless a test gives a workspace.
// Spectre's own CLI testing package is still an alpha.
internal static class SeedAppRunner {
  // The factory a test hands the command line in place of the real one: the shared database operations, container
  // tools, and clock (`SeedTestServer`), over the paths the test works on. `development` is the database `seed` may
  // drop, which a test that runs `seed` points at one of its own.
  public static SeedToolFactory FactoryFor(SeedPaths paths, string? development = null, ISnapshotSource? snapshots = null)
    => new(
      paths,
      SeedTestServer.Databases,
      SeedTestServer.Tools,
      SeedTestServer.Clock,
      development ?? DatabaseEnvironment.DevelopmentDatabase(),
      snapshots);

  // Over the real repository, which a test only reads.
  public static async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    => await RunAsync(services => services.AddSingleton(FactoryFor(SeedTestServer.Paths)), arguments);

  // Over a workspace: its paths and a factory for them. Only the container tools keep the real root, for the compose files.
  public static async Task<(int ExitCode, string Output)> RunAsync(SeedWorkspace workspace, params string[] arguments)
    => await RunAsync(
      services => {
        _ = services.AddSingleton(workspace.Paths);
        _ = services.AddSingleton(FactoryFor(workspace.Paths));
      },
      arguments);

  // For a test that replaces a registration, such as the factory that names the dev database a command may drop.
  public static async Task<(int ExitCode, string Output)> RunAsync(
    Action<IServiceCollection> configure,
    params string[] arguments
  ) {
    var services = new ServiceCollection();
    SeedApp.AddServices(services);
    configure(services);
    var console = new TestConsole();
    console.Profile.Width = 200;
    var app = new CommandApp(new ServiceCollectionRegistrar(services));
    app.Configure(config => SeedApp.Configure(config, console));
    var exitCode = await app.RunAsync(arguments, TestContext.Current.CancellationToken);
    return (exitCode, console.Output);
  }
}
