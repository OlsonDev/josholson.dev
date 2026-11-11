using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed bake` (`mise run seeds:bake`): builds the schema template and then every layer's template, parent first, and says
// which this run built and which were already current (ADR 0033, a scoped database per test). Needs the dev database
// server and the `tools` container.
public sealed class BakeCommand : AsyncCommand {
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;
  private readonly IAnsiConsole _console;

  public BakeCommand(SeedToolFactory factory, OwnerConnections connections, IAnsiConsole console) {
    _factory = factory;
    _connections = connections;
    _console = console;
  }

  public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken) {
    var tool = await _factory.OpenAsync(cancellationToken);
    var owner = await _connections.MaintenanceAsync(cancellationToken);
    var baked = await _console.Status()
      .StartAsync("Building templates", _ => tool.BakeAsync(owner, cancellationToken));
    var table = new Table()
      .AddColumn("Layer")
      .AddColumn("Template")
      .AddColumn("State");
    foreach (var (layer, name, built) in baked) {
      _ = table.AddRow(Markup.Escape(layer), Markup.Escape(name), built ? "[green]built[/]" : "[dim]current[/]");
    }
    _console.Write(table);
    return 0;
  }
}
