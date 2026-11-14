using JoshOlson.Database.Layers;
using NodaTime;

namespace JoshOlson.Database.Baking;

// A layer made from a database (`LayerExporter.ExportAsync`): the layer, the canonical text of its file, the anchor its relative
// times were measured from, and whether it replaces a layer of that name.
public sealed record LayerExport(SeedLayer Layer, string Yaml, Instant Anchor, bool Replaces);
