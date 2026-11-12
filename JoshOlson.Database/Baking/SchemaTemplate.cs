using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Npgsql;

namespace JoshOlson.Database.Baking;

// A schema template: a database with the migrations applied up to one version, the API role's grants in place, and the
// `seed` schema, which each layer's template and each test's database descend from (ADR 0033, a scoped database per
// test). The template for the newest version is the one everything day to day uses; one for an older version is what
// `seeds:migrate` puts a layer's rows into, the schema they were written for, before it runs the newer migrations over
// them (ADR 0034, lock file and branch-safe migration).
//
// It is built in four steps, the same for every version:
// 1. The versioned migrations up to the version, by Waypoint, from a directory of their own in the `tools` container.
//    (`--target` is no use: it still runs the repeatable migrations.)
// 2. Each repeatable migration, one at a time and each in a transaction, by Waypoint, so its history records every one
//    that applied, with its checksum, as it does for a database migrated by hand. The repeatables are written for the
//    newest schema, so at an older version one can fail for want of a table or column; that one is skipped and noted
//    (`SkippedAsync`) instead of failing the build, and leaves nothing in the history. At the newest version a failure
//    is an error. A skipped repeatable runs when the database goes on to the newest version: Waypoint sees it is not
//    in the history.
// 3. The API role's provisioning script.
// 4. The script that creates the `seed` schema, after the provisioning so the role is granted nothing in it.
//
// The name carries the version and a hash of everything that shapes the template (the version, the versioned migrations
// up to it, every repeatable, and both scripts), so a template that exists for the current files is simply reused, and
// one for older files is left for `seed:clean`. The build's outcome is a function of those files, so the same name always
// has the same skips. A template is built and closed by `ServerDatabases.EnsureTemplateAsync`, so a bake that died halfway
// never leaves one that looks complete, and a finished template refuses connections.
public sealed class SchemaTemplate {
  private readonly SchemaTemplateOptions _options;
  private readonly ComposeTools _tools;
  private readonly ServerDatabases _databases;

  public SchemaTemplate(SchemaTemplateOptions options, ComposeTools tools, ServerDatabases databases) {
    _options = options;
    _tools = tools;
    _databases = databases;
  }

  // The name of the template for the newest migration on disk.
  public async Task<string> NameAsync(CancellationToken cancellationToken)
    => await NameAsync(await NewestVersionAsync(cancellationToken), cancellationToken);

  public async Task<string> NameAsync(int version, CancellationToken cancellationToken) {
    var migrations = await VersionedUpToAsync(version, cancellationToken);
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(Encoding.UTF8.GetBytes($"version {version.ToInvariantString()}\n"));
    foreach (var migration in migrations) {
      hash.AppendData(Encoding.UTF8.GetBytes($"{migration.File} {migration.Checksum}\n"));
    }
    foreach (var path in RepeatablePaths()) {
      hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\n"));
      hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
    }
    hash.AppendData(Encoding.UTF8.GetBytes("provisioning\n"));
    hash.AppendData(await File.ReadAllBytesAsync(_options.ProvisioningScript, cancellationToken));
    hash.AppendData(Encoding.UTF8.GetBytes("seed schema\n"));
    hash.AppendData(await File.ReadAllBytesAsync(_options.SeedSchemaScript, cancellationToken));
    return SeedDatabaseNames.Schema(version, Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
  }

  // Makes sure the template for the newest migration exists and returns its name. `Built` says whether this call did the
  // work.
  public async Task<(string Name, bool Built)> EnsureAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    => await EnsureAsync(owner, await NewestVersionAsync(cancellationToken), cancellationToken);

  public async Task<(string Name, bool Built)> EnsureAsync(NpgsqlConnection owner, int version, CancellationToken cancellationToken) {
    var name = await NameAsync(version, cancellationToken);
    var newest = version == await NewestVersionAsync(cancellationToken);
    var built = await _databases.EnsureTemplateAsync(
      owner,
      name,
      async building => {
        await _databases.CreateAsync(owner, building, cancellationToken);
        await ApplyVersionedAsync(building, version, cancellationToken);
        var skipped = await ApplyRepeatablesAsync(building, version, newest, cancellationToken);
        await _tools.RunScriptAsync(building, _options.ProvisioningScript, ApiPasswordVariable(), cancellationToken);
        await _tools.RunScriptAsync(building, _options.SeedSchemaScript, [], cancellationToken);
        await _databases.CommentAsync(owner, building, JsonSerializer.Serialize(skipped), cancellationToken);
      },
      cancellationToken);
    return (name, built);
  }

  // The repeatable migrations the template for a version could not apply and went without, each with the error. Empty
  // when every one applied, which is always the case for the newest version. The template refuses connections, so the
  // note is kept in the database's comment, which can be read without one.
  public async Task<List<SkippedRepeatable>> SkippedAsync(NpgsqlConnection owner, string template, CancellationToken cancellationToken) {
    var comment = await _databases.ReadCommentAsync(owner, template, cancellationToken);
    return string.IsNullOrEmpty(comment) ? [] : JsonSerializer.Deserialize<List<SkippedRepeatable>>(comment) ?? [];
  }

  private async Task ApplyVersionedAsync(string database, int version, CancellationToken cancellationToken)
    => await MigrateFromAsync(database, await VersionedFilesAsync(version, cancellationToken), cancellationToken);

  private async Task<List<(string Name, byte[] Content)>> VersionedFilesAsync(int version, CancellationToken cancellationToken) {
    var files = new List<(string Name, byte[] Content)>();
    foreach (var migration in await VersionedUpToAsync(version, cancellationToken)) {
      files.Add((migration.File, await File.ReadAllBytesAsync(Path.Combine(_options.MigrationsDirectory, migration.File), cancellationToken)));
    }
    return files;
  }

  // Waypoint reads the files from a directory of their own in the container, which is gone afterwards. Everything pending
  // in it goes in one transaction.
  private async Task MigrateFromAsync(string database, List<(string Name, byte[] Content)> files, CancellationToken cancellationToken) {
    var directory = await _tools.PutMigrationsAsync(files, cancellationToken);
    try {
      await _tools.MigrateAsync(database, directory, transaction: true, cancellationToken);
    } finally {
      await _tools.RemoveMigrationsAsync(directory, CancellationToken.None);
    }
  }

  private async Task<List<SkippedRepeatable>> ApplyRepeatablesAsync(string database, int version, bool newest, CancellationToken cancellationToken) {
    var skipped = new List<SkippedRepeatable>();
    foreach (var path in RepeatablePaths()) {
      // The versioned migrations are already in the history, so they only have to be there for Waypoint to find them.
      var files = await VersionedFilesAsync(version, cancellationToken);
      files.Add((Path.GetFileName(path), await File.ReadAllBytesAsync(path, cancellationToken)));
      try {
        await MigrateFromAsync(database, files, cancellationToken);
      } catch (InvalidOperationException exception) when (!newest) {
        skipped.Add(new(Path.GetFileName(path), FirstError(exception.Message)));
      }
    }
    return skipped;
  }

  // The repeatable migrations by name, the order Waypoint runs them in.
  private List<string> RepeatablePaths()
    => [.. Directory.EnumerateFiles(_options.MigrationsDirectory, "R__*.sql").Order(StringComparer.Ordinal)];

  private async Task<int> NewestVersionAsync(CancellationToken cancellationToken)
    => (await VersionedUpToAsync(int.MaxValue, cancellationToken))[^1].Version;

  private async Task<List<MigrationFile>> VersionedUpToAsync(int version, CancellationToken cancellationToken) {
    var migrations = (await MigrationFiles.LoadAsync(_options.MigrationsDirectory, cancellationToken))
      .FindAll(migration => migration.Version <= version);
    return migrations.Count > 0
      ? migrations
      : throw new SeedFormatException($"There are no migrations up to version {version.ToInvariantString()}.");
  }

  // Waypoint's own line for what went wrong (`ERROR: Migration failed for R__foo.sql: relation "bar" does not exist`), not the
  // whole output.
  private static string FirstError(string output)
    => output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Contains("ERROR:", StringComparison.Ordinal))
      ?? output.Trim();

  private static Dictionary<string, string> ApiPasswordVariable()
    => new(StringComparer.Ordinal) {
      ["api_password"] = Environment.GetEnvironmentVariable("POSTGRES_API_PASSWORD")
        ?? throw new SeedFormatException("POSTGRES_API_PASSWORD is not set, and every seed template provisions the API role. Run through mise, which reads .env (CLAUDE.md)."),
    };
}
