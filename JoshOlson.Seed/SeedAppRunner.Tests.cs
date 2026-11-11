using JoshOlson.Database;
using JoshOlson.Seed.Composition;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace JoshOlson.Seed;

// Runs the real command line, wired as `Program` wires it, with the console swapped for one that keeps its output. The
// paths are the repository's unless a test gives its own. Spectre's own CLI testing package is still an alpha.
internal static class SeedAppRunner {
  public static async Task<(int ExitCode, string Output)> RunAsync(SeedPaths? paths, params string[] arguments)
    => await RunAsync(
      services => {
        if (paths is not null) {
          _ = services.AddSingleton(paths);
        }
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
