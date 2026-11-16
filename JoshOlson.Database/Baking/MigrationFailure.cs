namespace JoshOlson.Database.Baking;

// Waypoint ran and refused a migration: `Output` is the command and everything it printed. Not being able to run it at
// all (no container, no podman) is not this, and is thrown.
public sealed record MigrationFailure(string Output) {
  // Waypoint's own line for what went wrong (`ERROR: Migration failed for R__foo.sql: relation "bar" does not exist`),
  // not the whole output.
  public string FirstError
    => Output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Contains("ERROR:", StringComparison.Ordinal))
      ?? Output.Trim();
}
