using JoshOlson.Database.Baking;

namespace JoshOlson.Database.Seeding;

// What `SeedStateReader` found in a database: its rows, and what `seed.hydration` says about it (the anchor its relative
// times were measured from, the layer it was built from, and whether it was made for editing that layer).
public sealed record DatabaseState(SeedState Rows, HydrationRecord Hydration);
