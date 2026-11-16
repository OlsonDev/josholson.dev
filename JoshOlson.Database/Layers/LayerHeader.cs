namespace JoshOlson.Database.Layers;

// What a layer says about itself before its tables: its name, the layer it extends (null for a root), and its
// description. It is what making a layer from a difference needs besides the rows.
public sealed record LayerHeader(string Name, string? Extends, string Description);
