#!/usr/bin/env dotnet
#:project ../JoshOlson.Database
#:property PublishAot=false
// The seed tool's commands that have not moved to JoshOlson.Seed yet (every other one has), a thin layer over
// JoshOlson.Database (ADR 0033). Run through mise from the repo root:
//   mise run seeds:migrate   make migrations/schema.lock.yaml describe the dev database (see below)
//   mise run seeds:relock    the same, starting the lock over: for migrations rewritten before the first deploy
// 🚧 seeds:accept and seeds:check arrive with moving layers.
using JoshOlson.Database;
using JoshOlson.Database.Baking;
using JoshOlson.Database.Catalog;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Extensions;
using NodaTime;
using Npgsql;

var paths = SeedPaths.ForRepository();
var command = args.FirstOrDefault();
if (command is "migrate" or "relock") {
  return await MigrateAsync(paths, relock: command is "relock");
}
Console.Error.WriteLine("Usage: dotnet run scripts/seed.cs -- migrate|relock");
return 2;

// The dev database (POSTGRES_* from .env, which the mise task loads) must have applied exactly the migrations on disk:
// `mise run waypoint migrate` first. Writes migrations/schema.lock.yaml, or says everything wrong and writes nothing.
// `relock` starts the lock over (migrations rewritten before the first deploy); `migrate` carries it forward.
static async Task<int> MigrateAsync(SeedPaths paths, bool relock) {
  try {
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await connection.OpenAsync(CancellationToken.None);
    var options = new MigrateOptions {
      MigrationsDirectory = paths.Migrations,
      SeedsDirectory = paths.Seeds,
      Relock = relock,
      LayerCheck = (schema, cancellationToken) => CheckLayersAsync(paths, schema, cancellationToken),
    };
    var result = await SeedMigrator.MigrateAsync(connection, options, CancellationToken.None);
    var before = result.PreviousVersion is { } previous ? $"version {previous.ToInvariantString()}" : "no lock";
    Console.WriteLine(result.Changed
      ? $"Wrote {result.Path}: {before} to version {result.Version.ToInvariantString()}."
      : $"{result.Path} is already at version {result.Version.ToInvariantString()}.");
    return 0;
  } catch (Exception exception) when (exception is SeedFormatException or SchemaCatalogException) {
    Console.Error.WriteLine(exception.Message);
    return 1;
  }
}

// What `seeds:migrate` asks of the layers: every one must still build into a template from the migrations on disk.
static async Task<List<string>> CheckLayersAsync(SeedPaths paths, SeedSchema schema, CancellationToken cancellationToken) {
  try {
    var tool = await SeedTool.OpenAsync(paths, schema, SystemClock.Instance, cancellationToken);
    await using var owner = await OpenMaintenanceAsync();
    return await tool.CheckAsync(owner, cancellationToken);
  } catch (SeedFormatException exception) {
    return [exception.Message];
  }
}

static async Task<NpgsqlConnection> OpenMaintenanceAsync() {
  var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString("postgres"));
  await connection.OpenAsync(CancellationToken.None);
  return connection;
}
