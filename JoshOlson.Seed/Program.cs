using JoshOlson.Seed;
using JoshOlson.Seed.Composition;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
#if INCLUDE_TESTS
using Xunit.MicrosoftTestingPlatform;
using Xunit.Runner.InProc.SystemConsole;
#endif

#if INCLUDE_TESTS
if (IsTestRun(args)) {
  if (args.Any(arg => arg is "--server" or "--internal-msbuild-node"))
    return await TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions);
  return await ConsoleRunner.Run(args);
}
#endif

var services = new ServiceCollection();
SeedApp.AddServices(services);
var app = new CommandApp(new ServiceCollectionRegistrar(services));
app.Configure(config => SeedApp.Configure(config));

// Ctrl+C reaches the database calls as a canceled token instead of killing the process mid-statement.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
  e.Cancel = true;
  cancellation.Cancel();
};
return await app.RunAsync(args, cancellation.Token);

#if INCLUDE_TESTS
// `dotnet test` and IDE test runners pass Microsoft.Testing.Platform arguments the command line never gets.
static bool IsTestRun(string[] args)
  => Environment.GetEnvironmentVariable("JOSHOLSON_RUN_TESTS") is "1"
  || args.Any(arg => arg is "--server" or "--internal-msbuild-node" or "--list-tests"
    || arg.StartsWith("--dotnet-test-pipe", StringComparison.Ordinal)
    || arg.StartsWith("--results-directory", StringComparison.Ordinal));
#endif
