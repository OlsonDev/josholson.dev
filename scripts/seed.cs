#!/usr/bin/env dotnet
#:project ../JoshOlson.Database
#:property PublishAot=false
// The seed tool's command line, a thin layer over JoshOlson.Database (ADR 0033). Run through mise from the repo root:
//   mise run seeds:list      print the layer tree with row counts per table
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
using JoshOlson.Database.Locks;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Extensions;
using JoshOlson.Shared.IO;
using NodaTime;
using Npgsql;

const string SeedsDirectory = "JoshOlson.Database/seeds";
const string MigrationsDirectory = "JoshOlson.Database/migrations";

var command = args.FirstOrDefault();
if (command is "migrate" or "relock") {
  return await MigrateAsync(relock: command is "relock");
}
if (command is "bake" or "seed" or "test" or "clean") {
  return await ToolAsync(command, [.. args.Skip(1)]);
}
if (command is not "list") {
  Console.Error.WriteLine("Usage: dotnet run scripts/seed.cs -- list|migrate|relock|bake|seed <layer>|test <id> <layer>|clean [--all]");
  return 2;
}

LayerTree tree;
try {
  tree = await LoadTreeAsync();
} catch (SeedFormatException exception) {
  Console.Error.WriteLine(exception.Message);
  return 1;
}

foreach (var root in tree.Roots) {
  Print(root, 0);
}
Console.WriteLine($"{tree.Count} layer(s).");
return 0;

void Print(SeedLayer layer, int depth) {
  var indent = new string(' ', depth * 2);
  Console.WriteLine($"{indent}{layer.Name}: {layer.Description}");
  foreach (var table in layer.Tables) {
    Console.WriteLine($"{indent}  {table.Table}: +{table.Upserts.Count} -{table.Deletes.Count}");
  }
  foreach (var child in tree.ChildrenOf(layer.Name)) {
    Print(child, depth + 1);
  }
}

// The dev database (POSTGRES_* from .env, which the mise task loads) must have applied exactly the migrations on disk:
// `mise run waypoint migrate` first. Writes migrations/schema.lock.yaml, or says everything wrong and writes nothing.
// `relock` starts the lock over (migrations rewritten before the first deploy); `migrate` carries it forward.
static async Task<int> MigrateAsync(bool relock) {
  try {
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await connection.OpenAsync(CancellationToken.None);
    var options = new MigrateOptions {
      MigrationsDirectory = MigrationsDirectory,
      SeedsDirectory = SeedsDirectory,
      Relock = relock,
      LayerCheck = CheckLayersAsync,
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
static async Task<int> ToolAsync(string command, List<string> arguments) {
  try {
    var tool = await SeedTool.OpenAsync(RepositoryRoot.Find(), SystemClock.Instance, CancellationToken.None);
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
static async Task<List<string>> CheckLayersAsync(SeedSchema schema, CancellationToken cancellationToken) {
  try {
    var tool = await SeedTool.OpenAsync(RepositoryRoot.Find(), schema, SystemClock.Instance, cancellationToken);
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

// Layer values are typed by the lock, so the lock has to exist before there is a layer to read.
static async Task<LayerTree> LoadTreeAsync() {
  var lockPath = Path.Combine(MigrationsDirectory, SeedMigrator.LockFileName);
  if (!File.Exists(lockPath)) {
    return LayerLoader.Files(SeedsDirectory).Count > 0
      ? throw new SeedFormatException($"{lockPath} does not exist. Run mise run seeds:migrate first.")
      : new LayerTree([]);
  }
  var schemaLock = SchemaLockParser.Parse(await File.ReadAllTextAsync(lockPath, CancellationToken.None));
  return await LayerLoader.LoadAsync(SeedsDirectory, new SeedSchema(schemaLock), CancellationToken.None);
}
