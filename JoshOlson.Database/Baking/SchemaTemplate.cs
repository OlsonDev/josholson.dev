using System.Security.Cryptography;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Database.Baking;

// The schema template: a database with every migration applied and the API role's grants in place, which each layer's
// template and each test's database descend from (ADR 0033, a scoped database per test). Its name carries a hash of
// everything that shapes it (the migrations, repeatable ones included, and the provisioning script), so a template that
// exists for the current files is simply reused, and one for older files is left for `seed:clean`.
//
// It is built under a temporary name and renamed when finished, so a bake that died halfway never leaves a template
// that looks complete. A finished template refuses connections: it is only ever a source for `CREATE DATABASE`.
public sealed class SchemaTemplate {
  private readonly SchemaTemplateOptions _options;
  private readonly ComposeTools _tools;

  public SchemaTemplate(SchemaTemplateOptions options, ComposeTools tools) {
    _options = options;
    _tools = tools;
  }

  public async Task<string> NameAsync(CancellationToken cancellationToken) {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var path in Directory.EnumerateFiles(_options.MigrationsDirectory, "*.sql").Order(StringComparer.Ordinal)) {
      hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\n"));
      hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
    }
    hash.AppendData(System.Text.Encoding.UTF8.GetBytes("provisioning\n"));
    hash.AppendData(await File.ReadAllBytesAsync(_options.ProvisioningScript, cancellationToken));
    return SeedDatabaseNames.Schema(Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
  }

  // Makes sure the template for the current files exists and returns its name. `Built` says whether this call did the
  // work.
  public async Task<(string Name, bool Built)> EnsureAsync(NpgsqlConnection owner, CancellationToken cancellationToken) {
    var name = await NameAsync(cancellationToken);
    if (await ExistsAsync(owner, name, cancellationToken)) {
      return (name, false);
    }
    var building = SeedDatabaseNames.Building(name);
    await DropAsync(owner, building, cancellationToken);
    await owner.ExecuteAsync($"CREATE DATABASE {SeedDatabaseNames.Quote(building)};", null, cancellationToken);
    try {
      await _tools.MigrateAsync(building, cancellationToken);
      await _tools.RunScriptAsync(building, _options.ProvisioningScript, ApiPasswordVariable(), cancellationToken);
      await owner.ExecuteAsync(
        $"ALTER DATABASE {SeedDatabaseNames.Quote(building)} WITH ALLOW_CONNECTIONS false;",
        null,
        cancellationToken);
      await owner.ExecuteAsync(
        $"ALTER DATABASE {SeedDatabaseNames.Quote(building)} RENAME TO {SeedDatabaseNames.Quote(name)};",
        null,
        cancellationToken);
    } catch {
      await DropAsync(owner, building, CancellationToken.None);
      throw;
    }
    return (name, true);
  }

  public static async Task<bool> ExistsAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.QuerySingleAsync<bool>(
      "SELECT EXISTS (SELECT FROM pg_database WHERE datname = @name);",
      new { name },
      cancellationToken);

  public static async Task DropAsync(NpgsqlConnection owner, string name, CancellationToken cancellationToken)
    => await owner.ExecuteAsync(
      $"DROP DATABASE IF EXISTS {SeedDatabaseNames.Quote(name)} WITH (FORCE);",
      null,
      cancellationToken);

  private static Dictionary<string, string> ApiPasswordVariable()
    => new(StringComparer.Ordinal) {
      ["api_password"] = Environment.GetEnvironmentVariable("POSTGRES_API_PASSWORD")
        ?? throw new SeedFormatException("POSTGRES_API_PASSWORD is not set. Run through mise, which reads .env (CLAUDE.md)."),
    };
}
