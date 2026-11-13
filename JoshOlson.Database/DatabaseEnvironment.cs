using System.Globalization;
using Npgsql;

namespace JoshOlson.Database;

// How the seed tool reaches the dev database: as its owner (it reads the catalogs and, later, builds databases), through
// the POSTGRES_* settings in `.env` that the mise tasks load. The defaults are the dev compose file's.
public static class DatabaseEnvironment {
  public static string OwnerConnectionString()
    => Builder().ToString();

  // The same server and login, for another database. Pooling is off: a connection kept open by the pool would stop
  // the database being renamed, cloned, or dropped once the caller is done with it.
  public static string OwnerConnectionString(string database) {
    var builder = Builder();
    builder.Database = database;
    builder.Pooling = false;
    return builder.ToString();
  }

  // An open owner connection to the dev database, which the caller disposes.
  public static async Task<NpgsqlConnection> OpenOwnerAsync(CancellationToken cancellationToken)
    => await OpenAsync(OwnerConnectionString(), cancellationToken);

  // The same for another database on the server.
  public static async Task<NpgsqlConnection> OpenOwnerAsync(string database, CancellationToken cancellationToken)
    => await OpenAsync(OwnerConnectionString(database), cancellationToken);

  // The dev database's name: the one `seed <layer>` rebuilds.
  public static string DevelopmentDatabase()
    => Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "josholson";

  // A connection that cannot be opened is disposed here: the caller never gets it, so it could not.
  private static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken) {
    var connection = new NpgsqlConnection(connectionString);
    try {
      await connection.OpenAsync(cancellationToken);
    } catch {
      await connection.DisposeAsync();
      throw;
    }
    return connection;
  }

  private static NpgsqlConnectionStringBuilder Builder() {
    var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
      ?? throw new SeedFormatException("POSTGRES_PASSWORD is not set. Start the dev database and run through mise, which reads .env (CLAUDE.md).");
    return new() {
      Host = "127.0.0.1",
      Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432", CultureInfo.InvariantCulture),
      Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "josholson",
      Password = password,
      Database = DevelopmentDatabase(),
      SslMode = SslMode.Disable,
    };
  }
}
