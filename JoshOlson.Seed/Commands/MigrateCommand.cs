using System.ComponentModel;
using JoshOlson.Database;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed migrate [--relock]` (`mise run seeds:migrate`, `mise run seeds:relock`): writes `migrations/schema.lock.yaml` from
// the schema the migrations on disk produce, read from the template for the newest one (the dev database is not used), or
// says everything that is wrong and writes nothing. Each seed layer must still read against the new schema and build into a
// template, or the lock is not written (ADR 0034, lock file and branch-safe migration). `--relock` starts the lock over,
// ignoring the old one's checksums: for migrations rewritten before the first deploy.
public sealed class MigrateCommand : AsyncCommand<MigrateCommand.Settings> {
  private readonly SeedPaths _paths;
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;
  private readonly IAnsiConsole _console;

  public MigrateCommand(SeedPaths paths, SeedToolFactory factory, OwnerConnections connections, IAnsiConsole console) {
    _paths = paths;
    _factory = factory;
    _connections = connections;
    _console = console;
  }

  public sealed class Settings : CommandSettings {
    [CommandOption("--relock")]
    [Description("Start the lock over from the dev database, ignoring the old lock's checksums")]
    public bool Relock { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var owner = await _connections.MaintenanceAsync(cancellationToken);
    var options = new MigrateOptions {
      MigrationsDirectory = _paths.Migrations,
      SeedsDirectory = _paths.Seeds,
      Template = _factory.OpenSchemaTemplate(),
      Databases = _factory.Databases,
      Relock = settings.Relock,
      LayerCheck = CheckLayersAsync,
    };
    var result = await SeedMigrator.MigrateAsync(owner, options, cancellationToken);
    var before = result.PreviousVersion is { } previous ? $"version {previous.ToInvariantString()}" : "no lock";
    if (result.Changed) {
      _console.MarkupLineInterpolated(
        $"[green]Wrote[/] {result.Path}: {before} to version {result.Version.ToInvariantString()}.");
    } else {
      _console.MarkupLineInterpolated($"{result.Path} is already at version {result.Version.ToInvariantString()}.");
    }
    return 0;
  }

  // What the lock asks of the layers: every one must still build into a template from the migrations on disk. Layers
  // that cannot be read at all are a problem like any other.
  private async Task<List<string>> CheckLayersAsync(SeedSchema schema, CancellationToken cancellationToken) {
    try {
      var tool = await _factory.OpenAsync(schema, cancellationToken);
      var owner = await _connections.MaintenanceAsync(cancellationToken);
      return await tool.CheckAsync(owner, cancellationToken);
    } catch (SeedFormatException exception) {
      return [exception.Message];
    }
  }
}
