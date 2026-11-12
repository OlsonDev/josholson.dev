using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Migrate;

// A layer as `seeds:migrate` moved it: the layer at the newest schema and the canonical text of its file. An `Authored` one
// is somebody's own file that was kept as it is and checked against the newest schema (see `GeneratedLayers`), so its text
// is the file's, not the tool's.
public sealed record MovedLayer(SeedLayer Layer, string Yaml, bool Authored = false);
