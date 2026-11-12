namespace JoshOlson.Database.Migrate;

// What a source found: the snapshot (null when there is no base to read), and what it has to say about how it got it (a
// fetch that failed, a base it could not find), for the command to show.
public sealed record SnapshotRead(SeedSnapshot? Snapshot, List<string> Warnings);
