#!/usr/bin/env dotnet
#:project ../JoshOlson.Database
#:property PublishAot=false
// The seed tool's command line, a thin layer over JoshOlson.Database (ADR 0033). Run through mise from the repo root:
//   mise run seeds:list      print the layer tree with row counts per table
//   mise run seeds:migrate   make migrations/schema.lock.yaml describe the dev database (see below)
// 🚧 seeds:accept, seed, seed:test, and seed:clean arrive with the database side of the tool.
using JoshOlson.Database;
using JoshOlson.Database.Catalog;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Locks;
using JoshOlson.Database.Migrate;
using JoshOlson.Shared.Extensions;
using Npgsql;

const string SeedsDirectory = "JoshOlson.Database/seeds";
const string MigrationsDirectory = "JoshOlson.Database/migrations";

var command = args.FirstOrDefault();
if (command is "migrate") {
  return await MigrateAsync();
}
if (command is not "list") {
  Console.Error.WriteLine("Usage: dotnet run scripts/seed.cs -- list|migrate");
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
static async Task<int> MigrateAsync() {
  try {
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString());
    await connection.OpenAsync(CancellationToken.None);
    var options = new MigrateOptions {
      MigrationsDirectory = MigrationsDirectory,
      SeedsDirectory = SeedsDirectory,
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
