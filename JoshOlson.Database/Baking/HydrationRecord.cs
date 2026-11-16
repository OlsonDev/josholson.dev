using NodaTime;

namespace JoshOlson.Database.Baking;

// What `seed.hydration` says about a database: the anchor its relative times are measured from, the layer it was built
// from (null when it was made by the migrations alone), and whether it was built for editing that layer. It is what
// `SeedHydration` writes and what it reads back.
public sealed record HydrationRecord(Instant Anchor, string? Layer, bool Editing);
