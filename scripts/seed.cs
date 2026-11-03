#!/usr/bin/env dotnet
#:project ../JoshOlson.Database
#:property PublishAot=false
// The seed tool's command line, a thin layer over JoshOlson.Database (ADR 0033). Run through mise from the repo root:
//   mise run seeds:list   print the layer tree with row counts per table
// 🚧 bake, seed, seed:test, and seed:clean arrive with the database side of the tool.
using JoshOlson.Database;
using JoshOlson.Database.Layers;

const string SeedsDirectory = "JoshOlson.Database/seeds";

var command = args.FirstOrDefault();
if (command is not "list") {
  Console.Error.WriteLine("Usage: dotnet run scripts/seed.cs -- list");
  return 2;
}

LayerTree tree;
try {
  tree = await LayerLoader.LoadAsync(SeedsDirectory, CancellationToken.None);
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
