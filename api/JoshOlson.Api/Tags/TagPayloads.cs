using System.ComponentModel.DataAnnotations;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Validation;

namespace JoshOlson.Api.Tags;

// The fields a client writes and reads back unchanged.
public abstract record TagBase {
  [Required, Trimmed]
  public required string Name { get; init; }

  [Required, KebabCase]
  public required string Slug { get; init; }

  [Trimmed]
  public required string? Description { get; init; }

  [HexColor]
  public required string? Color { get; init; }
}

[ValidatableType]
public sealed record TagRequest : TagBase;

public sealed record TagResponse : TagBase {
  public required int TagId { get; init; }
}
