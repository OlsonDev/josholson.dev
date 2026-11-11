#!/usr/bin/env dotnet
#:project ../JoshOlson.Database
#:property PublishAot=false
// The seed tool's commands that have not moved to JoshOlson.Seed yet (`mise run seeds:list` has), a thin layer over
// JoshOlson.Database (ADR 0033). Run through mise from the repo root:
//   mise run seeds:migrate   make migrations/schema.lock.yaml describe the dev database (see below)
//   mise run seeds:relock    the same, starting the lock over: for migrations rewritten before the first deploy
//   mise run seeds:bake      build the schema template and every layer's template (a no-op when they are current)
//   mise run seed <layer>    drop the dev database and rebuild it from the layer, relative times measured from now
//   mise run seed:test <id> <layer>   a database of its own, seed_test_<id>, cloned from the layer's template
//   mise run seed:clean [--all]   drop the seed tool's databases the current files no longer name (all with --all)
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
if (command is "bake" or "seed" or "test" or "clean") {
  return await ToolAsync(paths, command, [.. args.Skip(1)]);
}
Console.Error.WriteLine("Usage: dotnet run scripts/seed.cs -- migrate|relock|bake|seed <layer>|test <id> <layer>|clean [--all]");
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

// The seed tool's database commands. They connect to the maintenance database, never the dev database: Postgres cannot
// drop or clone a database somebody is connected to.
static async Task<int> ToolAsync(SeedPaths paths, string command, List<string> arguments) {
  try {
    var tool = await SeedTool.OpenAsync(paths, SystemClock.Instance, CancellationToken.None);
    await using var owner = await OpenMaintenanceAsync();
    switch (command) {
      case "bake":
        foreach (var (layer, name, built) in await tool.BakeAsync(owner, CancellationToken.None)) {
          Console.WriteLine($"{layer}: {name} ({(built ? "built" : "current")})");
        }
        return 0;
      case "seed" when arguments is [var layer]:
        var rows = await tool.SeedDevelopmentAsync(owner, layer, CancellationToken.None);
        Console.WriteLine($"Rebuilt {tool.Development} from {layer}: {rows.ToInvariantString()} row(s).");
        return 0;
      case "test" when arguments is [var id, var layer]:
        Console.WriteLine(await tool.CreateTestDatabaseAsync(owner, id, layer, CancellationToken.None));
        return 0;
      case "clean" when arguments is [] or ["--all"]:
        var dropped = await tool.CleanAsync(owner, everything: arguments is ["--all"], SeedDatabaseNames.Prefix, CancellationToken.None);
        foreach (var name in dropped) {
          Console.WriteLine($"Dropped {name}");
        }
        Console.WriteLine($"{dropped.Count.ToInvariantString()} database(s) dropped.");
        return 0;
      default:
        Console.Error.WriteLine("Usage: dotnet run scripts/seed.cs -- bake|seed <layer>|test <id> <layer>|clean [--all]");
        return 2;
    }
  } catch (SeedFormatException exception) {
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
