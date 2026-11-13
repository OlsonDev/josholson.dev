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
// ignoring the old one's checksums: for migrations rewritten before the first deploy. `--base <ref>` and `--no-fetch` are
// for a branch: the lock and the layers the tool wrote are derived from `origin/main`'s (or the ref's) when a migration the
// branch has not merged is edited, which is fetched first unless told not to (ADR 0039, derive the lock and seeds from the
// base branch).
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

    [CommandOption("--base <REF>")]
    [Description("Derive the lock and layers from this commit's instead of the working tree's (default: origin/main, when it is needed)")]
    public string? Base { get; init; }

    [CommandOption("--no-fetch")]
    [Description("Do not fetch origin main before reading it")]
    public bool NoFetch { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var owner = await _connections.MaintenanceAsync(cancellationToken);
    var options = MigrateOptions.For(_paths, _factory.OpenSchemaTemplate(), _factory.Databases, _factory.Clock) with {
      Relock = settings.Relock,
      LayerCheck = CheckLayersAsync,
      Snapshots = _factory.OpenSnapshotSource(),
      Base = settings.Base,
      Fetch = !settings.NoFetch,
    };
    var result = await SeedMigrator.MigrateAsync(owner, options, cancellationToken);
    foreach (var warning in result.Warnings) {
      _console.MarkupLineInterpolated($"[yellow]Warning:[/] {warning}");
    }
    if (result.Base is not null) {
      _console.MarkupLineInterpolated($"Derived from {result.Base}.");
    }
    var before = result.PreviousVersion is { } previous ? $"version {previous.ToInvariantString()}" : "no lock";
    if (result.Changed) {
      _console.MarkupLineInterpolated(
        $"[green]Wrote[/] {result.Path}: {before} to version {result.Version.ToInvariantString()}.");
    } else {
      _console.MarkupLineInterpolated($"{result.Path} is already at version {result.Version.ToInvariantString()}.");
    }
    if (result.Layers.Count > 0) {
      var table = new Table().AddColumn("Layer").AddColumn("State");
      foreach (var (layer, rewritten) in result.Layers) {
        _ = table.AddRow(Markup.Escape(layer), rewritten ? "[green]rewritten[/]" : "[dim]unchanged[/]");
      }
      _console.Write(table);
    }
    if (result.Kept.Count > 0) {
      _console.MarkupLineInterpolated(
        $"[dim]Kept as they are, and checked against the new schema (your own files, not the tool's):[/] {string.Join(", ", result.Kept)}");
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
