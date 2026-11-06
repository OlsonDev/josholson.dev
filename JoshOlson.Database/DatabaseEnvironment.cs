using System.Globalization;
using Npgsql;

namespace JoshOlson.Database;

// How the seed tool reaches the dev database: as its owner (it reads the catalogs and, later, builds databases), through
// the POSTGRES_* settings in `.env` that the mise tasks load. The defaults are the dev compose file's.
public static class DatabaseEnvironment {
  public static string OwnerConnectionString() {
    var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
      ?? throw new SeedFormatException("POSTGRES_PASSWORD is not set. Start the dev database and run through mise, which reads .env (CLAUDE.md).");
    return new NpgsqlConnectionStringBuilder {
      Host = "127.0.0.1",
      Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432", CultureInfo.InvariantCulture),
      Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "josholson",
      Password = password,
      Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "josholson",
      SslMode = SslMode.Disable,
    }.ToString();
  }
}
