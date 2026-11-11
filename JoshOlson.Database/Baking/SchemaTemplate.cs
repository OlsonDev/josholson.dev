using System.Security.Cryptography;
using Npgsql;

namespace JoshOlson.Database.Baking;

// The schema template: a database with every migration applied and the API role's grants in place, which each layer's
// template and each test's database descend from (ADR 0033, a scoped database per test). Its name carries a hash of
// everything that shapes it (the migrations, repeatable ones included, the provisioning script, and the script that
// creates the `seed` schema), so a template that exists for the current files is simply reused, and one for older files
// is left for `seed:clean`.
//
// It is built and closed by `ServerDatabases.EnsureTemplateAsync`, so a bake that died halfway never leaves a template
// that looks complete, and a finished template refuses connections.
public sealed class SchemaTemplate {
  private readonly SchemaTemplateOptions _options;
  private readonly ComposeTools _tools;
  private readonly ServerDatabases _databases;

  public SchemaTemplate(SchemaTemplateOptions options, ComposeTools tools, ServerDatabases databases) {
    _options = options;
    _tools = tools;
    _databases = databases;
  }

  public async Task<string> NameAsync(CancellationToken cancellationToken) {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var path in Directory.EnumerateFiles(_options.MigrationsDirectory, "*.sql").Order(StringComparer.Ordinal)) {
      hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\n"));
      hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
    }
    hash.AppendData(System.Text.Encoding.UTF8.GetBytes("provisioning\n"));
    hash.AppendData(await File.ReadAllBytesAsync(_options.ProvisioningScript, cancellationToken));
    hash.AppendData(System.Text.Encoding.UTF8.GetBytes("seed schema\n"));
    hash.AppendData(await File.ReadAllBytesAsync(_options.SeedSchemaScript, cancellationToken));
    return SeedDatabaseNames.Schema(Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
  }

  // Makes sure the template for the current files exists and returns its name. `Built` says whether this call did the
  // work.
  public async Task<(string Name, bool Built)> EnsureAsync(NpgsqlConnection owner, CancellationToken cancellationToken) {
    var name = await NameAsync(cancellationToken);
    var built = await _databases.EnsureTemplateAsync(
      owner,
      name,
      async building => {
        await _databases.CreateAsync(owner, building, cancellationToken);
        await _tools.MigrateAsync(building, cancellationToken);
        await _tools.RunScriptAsync(building, _options.ProvisioningScript, ApiPasswordVariable(), cancellationToken);
        await _tools.RunScriptAsync(building, _options.SeedSchemaScript, [], cancellationToken);
      },
      cancellationToken);
    return (name, built);
  }

  private static Dictionary<string, string> ApiPasswordVariable()
    => new(StringComparer.Ordinal) {
      ["api_password"] = Environment.GetEnvironmentVariable("POSTGRES_API_PASSWORD")
        ?? throw new SeedFormatException("POSTGRES_API_PASSWORD is not set. Run through mise, which reads .env (CLAUDE.md)."),
    };
}
