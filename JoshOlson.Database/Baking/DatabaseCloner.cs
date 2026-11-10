using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Database.Baking;

// `CREATE DATABASE ... TEMPLATE`, which Postgres refuses (error 55006, "source database is being accessed by other
// users") for as long as anyone is connected to the source, even a connection that is just closing (ADR 0033, a
// scoped database per test). A template accepts no connections, so the wait is only ever for the last one to finish;
// the clone is tried again a few times before giving up.
public sealed class DatabaseCloner {
  private const string _SourceInUse = "55006";

  public int Attempts { get; init; } = 50;
  public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(100);

  // A clone is a seed tool database, like its source.
  public async Task CloneAsync(NpgsqlConnection owner, string source, string target, CancellationToken cancellationToken)
    => await CreateAsync(owner, SeedDatabaseNames.Quote(source), SeedDatabaseNames.Quote(target), cancellationToken);

  // The one exception to the prefix: the dev database, which `seed <layer>` makes afresh from a template.
  public async Task CloneDevelopmentAsync(
    NpgsqlConnection owner,
    string source,
    string development,
    CancellationToken cancellationToken
  )
    => await CreateAsync(owner, SeedDatabaseNames.Quote(source), SeedDatabaseNames.QuoteDevelopment(development), cancellationToken);

  private async Task CreateAsync(NpgsqlConnection owner, string source, string target, CancellationToken cancellationToken) {
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
