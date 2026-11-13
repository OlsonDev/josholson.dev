using JoshOlson.Database;
using JoshOlson.Database.Layers;
using JoshOlson.Shared.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed list` (`mise run seeds:list`): the layer tree, each layer with what it changes in each table (+ inserts, ~ updates, - deletes).
public sealed class ListCommand : AsyncCommand {
  private readonly SeedPaths _paths;
  private readonly IAnsiConsole _console;

  public ListCommand(SeedPaths paths, IAnsiConsole console) {
    _paths = paths;
    _console = console;
  }

  public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken) {
    var tree = await LoadTreeAsync(cancellationToken);
    var widget = new Tree("Seed layers");
    foreach (var root in tree.Roots) {
      Add(widget, tree, root);
    }
    _console.Write(widget);
    _console.WriteLine($"{tree.Count.ToInvariantString()} layer(s).");
    return 0;
  }

  private static void Add(IHasTreeNodes parent, LayerTree tree, SeedLayer layer) {
    var node = parent.AddNode($"[bold]{Markup.Escape(layer.Name)}[/]: {Markup.Escape(layer.Description)}");
    foreach (var table in layer.Tables) {
      _ = node.AddNode(
        $"{Markup.Escape(table.Table)}: [green]+{table.Inserts.Count.ToInvariantString()}[/] [yellow]~{table.Updates.Count.ToInvariantString()}[/] [red]-{table.Deletes.Count.ToInvariantString()}[/]");
    }
    foreach (var child in tree.ChildrenOf(layer.Name)) {
      Add(node, tree, child);
    }
  }

  // Layer values are typed by the lock, so the lock has to exist before there is a layer to read.
  // With neither a lock nor a layer there is nothing to list, and that is not an error.
  private async Task<LayerTree> LoadTreeAsync(CancellationToken cancellationToken)
    => File.Exists(_paths.LockFile) || LayerLoader.Files(_paths.Seeds).Count > 0
      ? await LayerLoader.LoadAsync(_paths.Seeds, await SeedSchema.LoadAsync(_paths, cancellationToken), cancellationToken)
      : new LayerTree([]);
}
