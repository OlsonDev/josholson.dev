namespace JoshOlson.Api.Tags;

// The fields a client writes and reads back unchanged.
public abstract record TagBase {
  public required string Name { get; init; }
  public required string Slug { get; init; }
  public required string? Description { get; init; }
  public required string? Color { get; init; }
}

public sealed record TagRequest : TagBase;

public sealed record TagResponse : TagBase {
  public required int TagId { get; init; }
}
