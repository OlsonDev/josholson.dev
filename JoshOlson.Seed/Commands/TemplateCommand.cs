using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed template <layer>` (`mise seed:template <layer>`): prints the name of the layer's template database, building it
// first if it is not current (ADR 0033, a scoped database per test). A caller that clones it itself (the browser tests,
// straight from the server and not through `seed test`, which costs a process each) asks once per run, since the name
// changes whenever the migrations or the layer chain do.
public sealed class TemplateCommand : AsyncCommand<TemplateCommand.Settings> {
  private readonly SeedToolFactory _factory;
  private readonly IAnsiConsole _console;

  public TemplateCommand(SeedToolFactory factory, IAnsiConsole console) {
    _factory = factory;
    _console = console;
  }

  public sealed class Settings : CommandSettings {
    [CommandArgument(0, "<layer>")]
    [Description("The layer whose template's name is printed, or `blank` for the schema template")]
    public required string Layer { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var tool = await _factory.OpenAsync(cancellationToken);
    _console.WriteLine(await tool.TemplateNameAsync(settings.Layer, cancellationToken));
    return 0;
  }
}
