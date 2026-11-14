using System.ComponentModel;
using JoshOlson.Shared.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed seed <layer>` (`mise run seed <layer>`): drops the dev database and makes it again from the schema template with
// the layer's rows in it, their relative times measured from now. Everything in the dev database is lost. Dev only. A
// plain build is for making a child of the layer; `--edit` is for changing the layer itself (see `export`).
public sealed class SeedCommand : AsyncCommand<SeedCommand.Settings> {
  private readonly SeedToolSession _session;
  private readonly IAnsiConsole _console;

  public SeedCommand(SeedToolSession session, IAnsiConsole console) {
    _session = session;
    _console = console;
  }

  public sealed class Settings : CommandSettings {
    [CommandArgument(0, "<layer>")]
    [Description("The layer to rebuild the dev database from, for example `more-tags`")]
    public required string Layer { get; init; }

    [CommandOption("--edit")]
    [Description("Change the layer itself: new rows take IDs from its own block, and the next export replaces it")]
    public bool Edit { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var (tool, owner) = await _session.OpenAsync(cancellationToken);
    var rows = await _console.Status()
      .StartAsync($"Rebuilding {tool.Development}", _ => tool.SeedDevelopmentAsync(owner, settings.Layer, settings.Edit, cancellationToken));
    _console.MarkupLineInterpolated(
      $"[green]Rebuilt[/] {tool.Development} from {settings.Layer}: {rows.ToInvariantString()} row(s).");
    return 0;
  }
}
