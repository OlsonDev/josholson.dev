using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JoshOlson.Seed.Commands;

// `seed test <id> <layer>` (`mise run seed:test <id> <layer>`): a database of its own, `seed_test_<id>`, cloned from the
// layer's template (ADR 0033, a scoped database per test), and prints its name. `seed clean` drops it.
public sealed class TestCommand : AsyncCommand<TestCommand.Settings> {
  private readonly SeedToolFactory _factory;
  private readonly OwnerConnections _connections;
  private readonly IAnsiConsole _console;

  public TestCommand(SeedToolFactory factory, OwnerConnections connections, IAnsiConsole console) {
    _factory = factory;
    _connections = connections;
    _console = console;
  }

  public sealed class Settings : CommandSettings {
    [CommandArgument(0, "<id>")]
    [Description("Lowercase letters and digits, at most 40")]
    public required string Id { get; init; }

    [CommandArgument(1, "<layer>")]
    [Description("The layer whose template the database is cloned from")]
    public required string Layer { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    var tool = await _factory.OpenAsync(cancellationToken);
    var owner = await _connections.MaintenanceAsync(cancellationToken);
    var name = await tool.CreateTestDatabaseAsync(owner, settings.Id, settings.Layer, cancellationToken);
    _console.WriteLine(name);
    return 0;
  }
}
