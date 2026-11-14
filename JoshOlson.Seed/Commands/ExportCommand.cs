using System.ComponentModel;
using JoshOlson.Database;
using JoshOlson.Shared.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed export --name <name> --description <text>` (`mise run seeds:export ...`): makes a layer from what is in a database
// and writes it to `seeds/<name>.yaml`. The flow it is for: `seed minimal`, use the app, then export what you added as a
// child of `minimal`, the layer the database records in `seed.hydration`. A database that records none makes a root
// layer. The layer is the difference from the parent's template (a root: from the schema template): `insert`s of new rows,
// `update`s of only the columns that changed, `delete`s of gone rows. The tables it found rows in are listed as it writes,
// so a row that should not be there (a real account you enrolled) is not a surprise in the diff.
public sealed class ExportCommand : AsyncCommand<ExportCommand.Settings> {
  private readonly SeedPaths _paths;
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;
  private readonly IAnsiConsole _console;

  public ExportCommand(SeedPaths paths, SeedToolFactory factory, OwnerConnections connections, IAnsiConsole console) {
    _paths = paths;
    _factory = factory;
    _connections = connections;
    _console = console;
  }

  public sealed class Settings : CommandSettings {
    [CommandOption("--name <NAME>")]
    [Description("The layer's name: lower-kebab-case, 40 characters at most (left out, an edited layer is replaced)")]
    public string? Name { get; init; }

    [CommandOption("--description <TEXT>")]
    [Description("A sentence about what the layer adds (an edited layer keeps its own)")]
    public string? Description { get; init; }

    [CommandOption("--database <DATABASE>")]
    [Description("The database to export (default: the dev database)")]
    public string? Database { get; init; }

    [CommandOption("--overwrite")]
    [Description("Replace a layer of that name, if no other layer extends it")]
    public bool Overwrite { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var tool = await _factory.OpenAsync(cancellationToken);
    var owner = await _connections.MaintenanceAsync(cancellationToken);
    var database = settings.Database ?? tool.Development;
    var export = await _console.Status().StartAsync(
      $"Exporting {database}",
      _ => tool.Exporter.ExportAsync(owner, database, settings.Name, settings.Description, settings.Overwrite, cancellationToken));

    var table = new Table()
      .AddColumn("Table")
      .AddColumn(new TableColumn("Insert").RightAligned())
      .AddColumn(new TableColumn("Update").RightAligned())
      .AddColumn(new TableColumn("Delete").RightAligned());
    foreach (var change in export.Layer.Tables) {
      _ = table.AddRow(
        Markup.Escape(change.Table),
        change.Inserts.Count.ToInvariantString(),
        change.Updates.Count.ToInvariantString(),
        change.Deletes.Count.ToInvariantString());
    }
    _console.Write(table);
    var path = _paths.Layer(export.Layer.Name);
    await File.WriteAllTextAsync(path, export.Yaml, cancellationToken);
    await tool.Exporter.RecordExportAsync(database, export, cancellationToken);
    _console.MarkupLineInterpolated($"[green]{(export.Replaces ? "Replaced" : "Wrote")}[/] {path}");
    return 0;
  }
}
