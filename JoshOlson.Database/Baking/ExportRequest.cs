namespace JoshOlson.Database.Baking;

// What `seeds:export` is asked for: the database to make a layer from, and what the command line said about the layer.
// `Name` and `Description` may be left out when the database was built for editing a layer, which is then replaced.
public sealed record ExportRequest {
  public required string Database { get; init; }
  public string? Name { get; init; }
  public string? Description { get; init; }

  // Replace a layer of that name that is already there.
  public bool Overwrite { get; init; }
}
