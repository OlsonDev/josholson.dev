using System.ComponentModel;
using JoshOlson.Database.Baking;
using JoshOlson.Shared.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed clean [--all]` (`mise run seed:clean`): drops the seed tool's databases that the current migrations and layers no
// longer name: old templates, test databases, and builds that died halfway. Never the dev database. `--all` drops the
// current templates too.
public sealed class CleanCommand : AsyncCommand<CleanCommand.Settings> {
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;
  private readonly IAnsiConsole _console;

  public CleanCommand(SeedToolFactory factory, OwnerConnections connections, IAnsiConsole console) {
    _factory = factory;
    _connections = connections;
    _console = console;
  }

  public sealed class Settings : CommandSettings {
    [CommandOption("--all")]
    [Description("Drop the current templates too")]
    public bool All { get; init; }

    [CommandOption("--prefix <PREFIX>")]
    [Description("Only consider databases whose names start with this, `seed_` or longer")]
    [DefaultValue(SeedDatabaseNames.Prefix)]
    public string Prefix { get; init; } = SeedDatabaseNames.Prefix;
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var tool = await _factory.OpenAsync(cancellationToken);
    var owner = await _connections.MaintenanceAsync(cancellationToken);
    var dropped = await tool.CleanAsync(owner, settings.All, settings.Prefix, cancellationToken);
    foreach (var name in dropped) {
      _console.MarkupLineInterpolated($"[red]Dropped[/] {name}");
    }
    _console.WriteLine($"{dropped.Count.ToInvariantString()} database(s) dropped.");
    return 0;
  }
}
