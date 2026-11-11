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
  public static async Task<(int ExitCode, string Output)> RunAsync(SeedPaths? paths, params string[] arguments) {
    var services = new ServiceCollection();
    SeedApp.AddServices(services);
    if (paths is not null) {
      _ = services.AddSingleton(paths);
    }
    var console = new TestConsole();
    console.Profile.Width = 200;
    var app = new CommandApp(new ServiceCollectionRegistrar(services));
    app.Configure(config => SeedApp.Configure(config, console));
    var exitCode = await app.RunAsync(arguments, TestContext.Current.CancellationToken);
    return (exitCode, console.Output);
  }
}
