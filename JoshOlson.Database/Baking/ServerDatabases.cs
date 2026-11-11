using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Database.Baking;

// Everything the seed tool does to the databases on the server as a whole, so that no other class writes `CREATE
// DATABASE`, `DROP DATABASE`, or `ALTER DATABASE`. Each method takes an owner connection to a database other than the
// one it works on (normally the maintenance database `postgres`): Postgres cannot drop, rename, or clone a database
// somebody is connected to. Names are checked by `SeedDatabaseNames` before they reach SQL, which is what lets them be
// quoted rather than passed as parameters (DDL takes no parameters). The dev database is the one name outside the
// `seed_` prefix, and has methods of its own so a seed tool name is never mistaken for it.
//
// `CREATE DATABASE ... TEMPLATE` is refused by Postgres (error 55006, "source database is being accessed by other
// users") for as long as anyone is connected to the source, even a connection that is just closing (ADR 0033, a
// scoped database per test). A template accepts no connections, so the wait is only ever for the last one to finish;
// the clone is tried again a few times before giving up.
public sealed class ServerDatabases {
  private const string _SourceInUse = "55006";

  public int Attempts { get; init; } = 50;
  public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(100);

  // Any name, not only a seed tool's: a test asks whether a database it did not expect is there.
  public async Task<bool> ExistsAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.QuerySingleAsync<bool>(
      "SELECT EXISTS (SELECT FROM pg_database WHERE datname = @name);",
      new { name },
      cancellationToken);

  // The databases whose names start with `prefix`, which has to be a seed tool name or a prefix of one (`seed_`).
  public async Task<List<string>> ListAsync(NpgsqlConnection owner, string prefix, CancellationToken cancellationToken) {
    _ = SeedDatabaseNames.Quote(prefix);
    return await owner.QueryAsync<string>(
      "SELECT datname FROM pg_database WHERE starts_with(datname, @prefix) ORDER BY datname;",
      new { prefix },
      cancellationToken);
  }

  public async Task CreateAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.ExecuteAsync($"CREATE DATABASE {SeedDatabaseNames.Quote(name)};", null, cancellationToken);

  // A clone is a seed tool database, like its source.
  public async Task CloneAsync(NpgsqlConnection owner, string source, string target, CancellationToken cancellationToken)
    => await CloneQuotedAsync(owner, SeedDatabaseNames.Quote(source), SeedDatabaseNames.Quote(target), cancellationToken);

  // The one exception to the prefix: the dev database, which `seed <layer>` makes afresh from a template.
  public async Task CloneDevelopmentAsync(
    NpgsqlConnection owner,
    string source,
    string development,
    CancellationToken cancellationToken
  )
    => await CloneQuotedAsync(owner, SeedDatabaseNames.Quote(source), SeedDatabaseNames.QuoteDevelopment(development), cancellationToken);

  // Does nothing when the database is not there. Connections to it are ended, so it goes even if something is still
  // attached.
  public async Task DropAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"DROP DATABASE IF EXISTS {SeedDatabaseNames.Quote(name)} WITH (FORCE);",
      null,
      cancellationToken);

  public async Task DropDevelopmentAsync(NpgsqlConnection owner, string development, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"DROP DATABASE IF EXISTS {SeedDatabaseNames.QuoteDevelopment(development)} WITH (FORCE);",
      null,
      cancellationToken);

  // A closed database refuses connections, which is what a template is: only ever a source for `CREATE DATABASE`.
  public async Task CloseAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"ALTER DATABASE {SeedDatabaseNames.Quote(name)} WITH ALLOW_CONNECTIONS false;",
      null,
      cancellationToken);

  public async Task RenameAsync(NpgsqlConnection owner, string name, string newName, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"ALTER DATABASE {SeedDatabaseNames.Quote(name)} RENAME TO {SeedDatabaseNames.Quote(newName)};",
      null,
      cancellationToken);

  // Makes sure the template `name` exists, running `build` with the name to build under if it does not, and says
  // whether it did. The template is built under a temporary name (`SeedDatabaseNames.Building`) and renamed when
  // `build` is done, so a build that died halfway never leaves a template that looks complete, and a later build
  // starts by dropping what it left. `build` makes the database it is handed, empty or cloned, and fills it. When it
  // throws, nothing is left behind. A finished template is closed.
  public async Task<bool> EnsureTemplateAsync(
    NpgsqlConnection owner,
    string name,
    Func<string, Task> build,
    CancellationToken cancellationToken
  ) {
    if (await ExistsAsync(owner, name, cancellationToken)) {
      return false;
    }
    var building = SeedDatabaseNames.Building(name);
    await DropAsync(owner, building, cancellationToken);
    try {
      await build(building);
      await CloseAsync(owner, building, cancellationToken);
      await RenameAsync(owner, building, name, cancellationToken);
    } catch {
      await DropAsync(owner, building, CancellationToken.None);
      throw;
    }
    return true;
  }

  private async Task CloneQuotedAsync(NpgsqlConnection owner, string source, string target, CancellationToken cancellationToken) {
    var sql = $"CREATE DATABASE {target} TEMPLATE {source};";
    for (var attempt = 1; ; attempt++) {
      try {
        await owner.ExecuteAsync(sql, null, cancellationToken);
        return;
      } catch (PostgresException exception) when (exception.SqlState is _SourceInUse && attempt < Attempts) {
        await Task.Delay(Delay, cancellationToken);
      }
    }
  }
}
