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
  private const string _MaintenanceDatabase = "postgres";

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
    => await owner.ExecuteAsync($"CREATE DATABASE {SeedDatabaseNames.Quote(name)};", cancellationToken);

  // A clone is a seed tool database, like its source.
  public async Task CloneAsync(NpgsqlConnection owner, string source, string target, CancellationToken cancellationToken)
    => await CloneQuotedAsync(owner, SeedDatabaseNames.Quote(source), SeedDatabaseNames.Quote(target), cancellationToken);

  // A clone under a new name that says what it is for (`SeedDatabaseNames.Scratch`), dropped when it is disposed.
  public async Task<ScratchClone> CloneScratchAsync(
    NpgsqlConnection owner,
    string source,
    string purpose,
    CancellationToken cancellationToken
  ) {
    var name = SeedDatabaseNames.Scratch(purpose);
    await CloneAsync(owner, source, name, cancellationToken);
    return new(this, owner, name);
  }

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
      cancellationToken);

  public async Task DropDevelopmentAsync(NpgsqlConnection owner, string development, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"DROP DATABASE IF EXISTS {SeedDatabaseNames.QuoteDevelopment(development)} WITH (FORCE);",
      cancellationToken);

  // A note on a database, kept in the server's catalog: it is read without connecting to the database, which a template
  // does not allow, and stays with the database when it is renamed. A clone does not get it.
  public async Task CommentAsync(NpgsqlConnection owner, string name, string comment, CancellationToken cancellationToken) {
    _ = SeedDatabaseNames.Quote(name);
    var statement = await owner.QuerySingleAsync<string>(
      "SELECT format('COMMENT ON DATABASE %I IS %L', @name, @comment);",
      new { name, comment },
      cancellationToken);
    await owner.ExecuteAsync(statement, cancellationToken);
  }

  public async Task<string?> ReadCommentAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.QuerySingleOrDefaultAsync<string?>(
      "SELECT shobj_description(d.oid, 'pg_database') FROM pg_database AS d WHERE d.datname = @name;",
      new { name },
      cancellationToken);

  // A closed database refuses connections, which is what a template is: only ever a source for `CREATE DATABASE`.
  public async Task CloseAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"ALTER DATABASE {SeedDatabaseNames.Quote(name)} WITH ALLOW_CONNECTIONS false;",
      cancellationToken);

  public async Task RenameAsync(NpgsqlConnection owner, string name, string newName, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"ALTER DATABASE {SeedDatabaseNames.Quote(name)} RENAME TO {SeedDatabaseNames.Quote(newName)};",
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
    // Two processes can want the same template at once (`mise run test` runs the test projects side by side), and the
    // second would drop the first's half-built database. A lock on the name makes it wait, and find the template there
    // when it gets its turn.
    await using var guard = await LockAsync(owner, name, cancellationToken);
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

  // An advisory lock belongs to one database, so two callers connected to different ones (the commands use the maintenance
  // database, the tests the dev one) would never meet. This takes it on a connection of its own to the maintenance
  // database, made from the caller's (`CloneWith` keeps its password), and the lock lasts until that connection is
  // disposed, or the process dies.
  private static async Task<NpgsqlConnection> LockAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken) {
    var builder = new NpgsqlConnectionStringBuilder(owner.ConnectionString) {
      Database = _MaintenanceDatabase,
      Pooling = false,
    };
    var guard = await owner.CloneWithAsync(builder.ToString(), cancellationToken);
    try {
      await guard.OpenAsync(cancellationToken);
      _ = await guard.ExecuteAsync("SELECT pg_advisory_lock(hashtextextended(@name, 0));", new { name }, cancellationToken);
    } catch {
      await guard.DisposeAsync();
      throw;
    }
    return guard;
  }

  private async Task CloneQuotedAsync(NpgsqlConnection owner, string source, string target, CancellationToken cancellationToken) {
    var sql = $"CREATE DATABASE {target} TEMPLATE {source};";
    for (var attempt = 1; ; attempt++) {
      try {
        await owner.ExecuteAsync(sql, cancellationToken);
        return;
      } catch (PostgresException exception) when (exception.SqlState is _SourceInUse && attempt < Attempts) {
        await Task.Delay(Delay, cancellationToken);
      }
    }
  }
}
