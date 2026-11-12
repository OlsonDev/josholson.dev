using JoshOlson.Database.Layers;

namespace JoshOlson.Database.Migrate;

// A layer as `seeds:migrate` moved it: the layer at the newest schema and the canonical text of its file.
public sealed record MovedLayer(SeedLayer Layer, string Yaml);
