namespace JoshOlson.Database.Locks;

// A layer file `seeds:migrate` wrote, and the SHA-256 of it as it wrote it. A file that still hashes to this was made by the
// tool and nobody has touched it, so the tool may write it again; any other file is somebody's own work, which the tool
// moves forward like the rest but never replaces with what it made, and does not record.
public sealed record LockedLayer(string Name, string Checksum);
