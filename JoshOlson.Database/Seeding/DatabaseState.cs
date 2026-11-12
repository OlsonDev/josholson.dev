using NodaTime;

namespace JoshOlson.Database.Seeding;

// What `SeedStateReader` found in a database: its rows, the anchor its relative times were measured from, and the layer it
// was built from (null when `seed.hydration` does not say), and whether it was made for editing that layer.
public sealed record DatabaseState(SeedState Rows, Instant Anchor, string? Layer, bool Editing);
