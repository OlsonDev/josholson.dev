namespace JoshOlson.Database.Migrate;

// The lock and the layer files as they are at another commit (the base: what the branch is measured from, `origin/main`
// unless a ref is given), read without changing the working tree. `LockText` is null when the base has no lock yet.
// `Description` names the commit for a message ("origin/main at 3f9a12b (2026-11-12)"), and `Commit` is its full hash, for
// a command a message suggests.
public sealed record SeedSnapshot(string Description, string Commit, string? LockText, Dictionary<string, string> Layers);
