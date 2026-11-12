using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Catalog;
using JoshOlson.Seed.Commands;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed;

// What the command line is made of: the services every command can take in its constructor, and the commands. `Program`
// and the tests both build the app from these, so a test runs the same wiring the tool does.
public static class SeedApp {
  public static void AddServices(IServiceCollection services) {
    _ = services.AddSingleton(SeedPaths.ForRepository());
    _ = services.AddSingleton<IClock>(SystemClock.Instance);
    _ = services.AddSingleton<ServerDatabases>();
    _ = services.AddSingleton(provider => new ComposeTools(provider.GetRequiredService<SeedPaths>().Root));
    _ = services.AddSingleton(provider => new SeedToolFactory(
      provider.GetRequiredService<SeedPaths>(),
      provider.GetRequiredService<ServerDatabases>(),
      provider.GetRequiredService<ComposeTools>(),
      provider.GetRequiredService<IClock>(),
      DatabaseEnvironment.DevelopmentDatabase()));
    _ = services.AddSingleton<OwnerConnections>();
  }

  // The console is the terminal unless a test gives its own. The handler needs it spelled out: a mistake in the command
  // line is thrown before the container exists, so there is nothing to resolve it from.
  public static void Configure(IConfigurator config, IAnsiConsole? console = null) {
    console ??= AnsiConsole.Console;
    config.ConfigureConsole(console);
    config.SetApplicationName("seed");
    _ = config.AddCommand<ListCommand>("list").WithDescription("Print the seed layer tree with row counts per table");
    _ = config.AddCommand<MigrateCommand>("migrate")
      .WithDescription("Make migrations/schema.lock.yaml describe the dev database at the newest migration");
    _ = config.AddCommand<ExportCommand>("export")
      .WithDescription("Make a seed layer from what is in a database (default: the dev database)");
    _ = config.AddCommand<CheckCommand>("check").WithDescription("Check that the lock is current and every seed layer is canonical, builds, and exports as itself");
    _ = config.AddCommand<BakeCommand>("bake").WithDescription("Build the schema template and every seed layer's template");
    _ = config.AddCommand<SeedCommand>("seed")
      .WithDescription("Drop the dev database and rebuild it from a seed layer, relative times measured from now");
    _ = config.AddCommand<TestCommand>("test")
      .WithDescription("Make a scoped database seed_test_<id> from a seed layer");
    _ = config.AddCommand<CleanCommand>("clean")
      .WithDescription("Drop the seed tool's stale databases (--all: the current templates too)");
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
