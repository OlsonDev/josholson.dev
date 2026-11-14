using JoshOlson.Database;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Seed.Commands;

// What the tests of `export` share: a workspace with the fixture layers for the command to write into, databases built
// from those layers by the shared tool (a template's name is a hash of the files, so the workspace's are the same
// ones), and the databases to drop when the test is done.
internal sealed class ExportCommandHarness : IAsyncDisposable {
  private readonly List<string> _created = [];
  private CancellationToken _cancellationToken;
  private NpgsqlConnection? _owner;
  private SeedWorkspace? _workspace;

  public SeedWorkspace Workspace
    => _workspace ?? throw new InvalidOperationException("The harness is not initialized.");

  private NpgsqlConnection Owner
    => _owner ?? throw new InvalidOperationException("The harness is not initialized.");

  public async Task InitializeAsync(CancellationToken cancellationToken) {
    _cancellationToken = cancellationToken;
    _workspace = await FixtureSeeds.WorkspaceAsync(cancellationToken);
    _owner = await SeedTestServer.OpenMaintenanceAsync(cancellationToken);
  }

  public async ValueTask DisposeAsync() {
    if (_owner is not null) {
      foreach (var name in _created) {
        await SeedTestServer.Databases.DropAsync(_owner, name, CancellationToken.None);
      }
      await _owner.DisposeAsync();
    }
    _workspace?.Dispose();
  }

  public async Task<string> DatabaseFromAsync(string layer) {
    var tool = await FixtureSeeds.Tool.WaitAsync(_cancellationToken);
    var name = await tool.CreateTestDatabaseAsync(Owner, Guid.NewGuid().ToString("N"), layer, _cancellationToken);
    _created.Add(name);
    return name;
  }

  public async Task RunSqlAsync(string database, string sql) {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, _cancellationToken);
    await connection.ExecuteAsync(sql, _cancellationToken);
  }

  // `seed export --database <database>` and whatever else the test passes.
  public async Task<(int ExitCode, string Output)> ExportAsync(string database, params string[] arguments)
    => await SeedAppRunner.RunAsync(Workspace, ["export", "--database", database, .. arguments]);

  public async Task<string> ReadLayerAsync(string name)
    => await Workspace.ReadLayerAsync(name, _cancellationToken);
}
