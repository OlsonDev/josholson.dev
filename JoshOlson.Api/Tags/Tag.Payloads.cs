using System.ComponentModel.DataAnnotations;
using JoshOlson.Api.Common.Validation;
using Microsoft.Extensions.Validation;

namespace JoshOlson.Api.Tags;

/// <summary>The fields a client writes to a tag and reads back unchanged.</summary>
public abstract record TagBase {
  /// <summary>What the tag is called. Not empty, and no other tag has the same name, ignoring case.</summary>
  [Required, Trimmed]
  public required string Name { get; init; }

  /// <summary>
  /// The tag as it appears in a URL: lowercase words joined by single hyphens, such as foo-bar-42. No other tag has the
  /// same slug.
  /// </summary>
  [Required, KebabCase]
  public required string Slug { get; init; }

  /// <summary>A sentence or two about the tag, or null for none.</summary>
  [Trimmed]
  public required string? Description { get; init; }

  /// <summary>The tag's color as a lowercase hex code such as #a1b2c3, or null for none.</summary>
  [HexColor]
  public required string? Color { get; init; }
}

/// <summary>What a client sends to create a tag or to replace one.</summary>
[ValidatableType]
public sealed record TagRequest : TagBase;

/// <summary>A tag as the API returns it.</summary>
public sealed record TagResponse : TagBase {
  /// <summary>The tag's ID, which its URL names.</summary>
  public required int TagId { get; init; }
}
