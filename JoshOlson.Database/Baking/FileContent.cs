namespace JoshOlson.Database.Baking;

// A file handed to a container by its name and bytes, not mounted: a migration for Waypoint to apply.
public sealed record FileContent(string Name, byte[] Content);
