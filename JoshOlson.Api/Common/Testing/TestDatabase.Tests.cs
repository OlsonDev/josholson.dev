using System.Globalization;
using Npgsql;

namespace JoshOlson.Api.Common.Testing;

// Tests run against the dev database as the DML-only `josholson_api` role, the way the API does (ADR 0018), so a
// missing grant fails here and not in production. `mise run db:provision` creates the role. POSTGRES_* come from the
// environment (`mise run test` loads them), falling back to the repo's `.env` so IDE test runners work without setup.
public static class TestDatabase {
  private static readonly Dictionary<string, string> _DotEnv = ReadDotEnv();

  public static string ConnectionString { get; } = new NpgsqlConnectionStringBuilder {
    Host = "127.0.0.1",
    Port = int.Parse(Setting("POSTGRES_PORT") ?? "5432", CultureInfo.InvariantCulture),
    Username = "josholson_api",
    Password = Setting("POSTGRES_API_PASSWORD")
      ?? throw new InvalidOperationException(
        "POSTGRES_API_PASSWORD is not set in the environment or in `.env`. Add it, then run `mise run db:provision`."),
    Database = Setting("POSTGRES_DB") ?? "josholson",
    SslMode = SslMode.Disable,
  }.ToString();

  // Connections from here read and write NodaTime types, as the API's data source does.
  public static NpgsqlDataSource DataSource { get; }
    = new NpgsqlDataSourceBuilder(ConnectionString).UseNodaTime().Build();

  private static string? Setting(string name)
    => Environment.GetEnvironmentVariable(name) ?? _DotEnv.GetValueOrDefault(name);

  private static Dictionary<string, string> ReadDotEnv() {
    var settings = new Dictionary<string, string>();
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
      var path = Path.Combine(directory.FullName, ".env");
      if (!File.Exists(path)) continue;
      foreach (var line in File.ReadLines(path)) {
        var separator = line.IndexOf('=');
        if (separator < 1 || line.TrimStart().StartsWith('#')) continue;
        settings[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"', '\'');
      }
      break;
    }
    return settings;
  }
}
