using System.ComponentModel;
using JoshOlson.Shared.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed seed <layer>` (`mise run seed <layer>`): drops the dev database and makes it again from the schema template with
// the layer's rows in it, their relative times measured from now. Everything in the dev database is lost. Dev only.
public sealed class SeedCommand : AsyncCommand<SeedCommand.Settings> {
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;
  private readonly IAnsiConsole _console;

  public SeedCommand(SeedToolFactory factory, OwnerConnections connections, IAnsiConsole console) {
    _factory = factory;
    _connections = connections;
    _console = console;
  }

  public sealed class Settings : CommandSettings {
    [CommandArgument(0, "<layer>")]
    [Description("The layer to rebuild the dev database from, for example `more-tags`")]
    public required string Layer { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var tool = await _factory.OpenAsync(cancellationToken);
    var owner = await _connections.MaintenanceAsync(cancellationToken);
    var rows = await _console.Status()
      .StartAsync($"Rebuilding {tool.Development}", _ => tool.SeedDevelopmentAsync(owner, settings.Layer, cancellationToken));
    _console.MarkupLineInterpolated(
      $"[green]Rebuilt[/] {tool.Development} from {settings.Layer}: {rows.ToInvariantString()} row(s).");
    return 0;
  }
}
