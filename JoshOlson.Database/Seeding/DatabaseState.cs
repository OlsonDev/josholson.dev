using NodaTime;

namespace JoshOlson.Database.Seeding;

// What `SeedStateReader` found in a database: its rows, and the anchor its relative times were measured from.
public sealed record DatabaseState(SeedState Rows, Instant Anchor);
