using JoshOlson.Database;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed check` (`mise run seeds:check`): says whether the seeds are current, and changes nothing. The lock must describe
// the migrations on disk and be at the newest one, and every layer must be in the canonical form the tool writes, build
// into a template, and give itself back (a database built from it, read and diffed against its parent's template, is
// the layer's own text). Every problem is listed, and the exit code is 1 if there is one, so a hook or CI can run it.
// Needs the dev database server and the `tools` container to build a template that is not current.
public sealed class CheckCommand : AsyncCommand {
  private readonly SeedPaths _paths;
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;
  private readonly IAnsiConsole _console;

  public CheckCommand(SeedPaths paths, SeedToolFactory factory, OwnerConnections connections, IAnsiConsole console) {
    _paths = paths;
    _factory = factory;
    _connections = connections;
    _console = console;
  }

  public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken) {
    var problems = await SeedMigrator.CheckLockAsync(_paths.Migrations, cancellationToken);
    var layers = 0;
    // The layers are read against the lock, and built from the migrations: neither means anything while the lock is off.
    if (problems.Count is 0) {
      var tool = await _factory.OpenAsync(cancellationToken);
      var owner = await _connections.MaintenanceAsync(cancellationToken);
      problems = await _console.Status().StartAsync("Checking the layers", _ => tool.CheckFilesAsync(owner, _paths.Seeds, cancellationToken));
      layers = tool.Tree.Count;
    }
    if (problems.Count is 0) {
      _console.MarkupLineInterpolated($"[green]The seeds are current[/]: {layers.ToInvariantString()} layer(s).");
      return 0;
    }
    foreach (var problem in problems) {
      _console.MarkupLineInterpolated($"[red]-[/] {problem}");
    }
    return 1;
  }
}
