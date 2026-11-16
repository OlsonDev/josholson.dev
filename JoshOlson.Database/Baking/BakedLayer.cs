namespace JoshOlson.Database.Baking;

// One line of what a bake did: a layer (or `(schema)` for the schema template), its template's name, and whether this
// bake built it.
public sealed record BakedLayer(string Layer, string Name, bool Built);
