using System.Security.Cryptography;
using System.Text;
using JoshOlson.Database.Locks;
using JoshOlson.Shared.Extensions;
using Npgsql;

namespace JoshOlson.Database.Baking;

// A template with the migrations applied only up to one version, and the `seed` schema: what `seeds:migrate` needs to
// put a layer's rows in the schema they were written for before it runs the newer migrations over them (ADR 0034, lock
// file and branch-safe migration). The versioned migrations up to that version are applied from a directory of their own,
// so the repeatable ones, which are written for the newest schema and fail on an older one, are never run; they apply
// with the rest when the database goes on to the newest version. The API role is not provisioned: nothing here is used
// through it.
//
// Like the schema template, its name carries a hash of what shapes it (the version, the migrations up to it, and the
// script that creates the `seed` schema), so one that exists for the current files is reused, and `seed:clean` drops it
// when it is old. It is built and closed by `ServerDatabases.EnsureTemplateAsync`.
public sealed class VersionedSchemaTemplate {
  private readonly SchemaTemplateOptions _options;
  private readonly ComposeTools _tools;
  private readonly ServerDatabases _databases;

  public VersionedSchemaTemplate(SchemaTemplateOptions options, ComposeTools tools, ServerDatabases databases) {
    _options = options;
    _tools = tools;
    _databases = databases;
  }

  public async Task<string> NameAsync(int version, CancellationToken cancellationToken) {
    var migrations = await UpToAsync(version, cancellationToken);
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(Encoding.UTF8.GetBytes($"version {version.ToInvariantString()}\n"));
    foreach (var migration in migrations) {
      hash.AppendData(Encoding.UTF8.GetBytes($"{migration.File} {migration.Checksum}\n"));
    }
    hash.AppendData(Encoding.UTF8.GetBytes("seed schema\n"));
    hash.AppendData(await File.ReadAllBytesAsync(_options.SeedSchemaScript, cancellationToken));
    return SeedDatabaseNames.Schema(Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
  }

  // Makes sure the template for the version exists and returns its name. `Built` says whether this call did the work.
  public async Task<(string Name, bool Built)> EnsureAsync(NpgsqlConnection owner, int version, CancellationToken cancellationToken) {
    var name = await NameAsync(version, cancellationToken);
    var built = await _databases.EnsureTemplateAsync(
      owner,
      name,
      async building => {
        await _databases.CreateAsync(owner, building, cancellationToken);
        var directory = await _tools.PutMigrationsAsync(await FilesUpToAsync(version, cancellationToken), cancellationToken);
        try {
          await _tools.MigrateAsync(building, directory, cancellationToken);
        } finally {
          await _tools.RemoveMigrationsAsync(directory, CancellationToken.None);
        }
        await _tools.RunScriptAsync(building, _options.SeedSchemaScript, [], cancellationToken);
      },
      cancellationToken);
    return (name, built);
  }

  private async Task<List<MigrationFile>> UpToAsync(int version, CancellationToken cancellationToken) {
    var migrations = (await MigrationFiles.LoadAsync(_options.MigrationsDirectory, cancellationToken))
      .FindAll(migration => migration.Version <= version);
    return migrations.Count > 0
      ? migrations
      : throw new SeedFormatException($"There are no migrations up to version {version.ToInvariantString()}.");
  }

  private async Task<List<(string Name, byte[] Content)>> FilesUpToAsync(int version, CancellationToken cancellationToken) {
    var files = new List<(string Name, byte[] Content)>();
    foreach (var migration in await UpToAsync(version, cancellationToken)) {
      files.Add((migration.File, await File.ReadAllBytesAsync(Path.Combine(_options.MigrationsDirectory, migration.File), cancellationToken)));
    }
    return files;
  }
}
