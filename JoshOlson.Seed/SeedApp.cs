using JoshOlson.Database;
using JoshOlson.Database.Catalog;
using JoshOlson.Seed.Commands;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed;

// What the command line is made of: the services every command can take in its constructor, and the commands. `Program`
// and the tests both build the app from these, so a test runs the same wiring the tool does.
public static class SeedApp {
  public static void AddServices(IServiceCollection services)
    => _ = services.AddSingleton(SeedPaths.ForRepository());

  // The console is the terminal unless a test gives its own. The handler needs it spelled out: a mistake in the command
  // line is thrown before the container exists, so there is nothing to resolve it from.
  public static void Configure(IConfigurator config, IAnsiConsole? console = null) {
    console ??= AnsiConsole.Console;
    config.ConfigureConsole(console);
    config.SetApplicationName("seed");
    _ = config.AddCommand<ListCommand>("list").WithDescription("Print the seed layer tree with row counts per table");
    config.SetExceptionHandler((exception, _) => HandleException(exception, console));
  }

  // A problem with the files or the database that the message explains is a failed run, not a crash: say it and exit 1,
  // so no command needs a try/catch of its own. A mistake in the command line is exit 2. Anything else is a bug, and
  // gets its stack trace.
  public static int HandleException(Exception exception, IAnsiConsole console) {
    if (exception is SeedFormatException or SchemaCatalogException) {
      console.MarkupLineInterpolated($"[red]{exception.Message}[/]");
      return 1;
    }
    if (exception is CommandAppException) {
      console.MarkupLineInterpolated($"[red]{exception.Message}[/]");
      return 2;
    }
    console.WriteException(exception, ExceptionFormats.ShortenEverything);
    return 1;
  }
}
