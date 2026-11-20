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
  /// same slug, current or previous. Saving a different slug keeps the old one as a previous slug, so its URL still
  /// works.
  /// </summary>
  [Required, KebabCase]
  public required string Slug { get; init; }

  /// <summary>Markdown about the tag, or null for none.</summary>
  [Trimmed]
  public required string? Description { get; init; }

  /// <summary>
  /// The tag's color as a Tailwind color name for the light-mode background, such as emerald-200, black, or white. Required
  /// when writing. Dark mode uses the mirrored weight (emerald-800).
  /// </summary>
  [Required, TailwindColor]
  public required string? Color { get; init; }

  /// <summary>
  /// Other names the tag goes by, such as JS for JavaScript. None is empty, and none is another tag's name or alias, or
  /// this tag's own name, ignoring case.
  /// </summary>
  public required List<string> Aliases { get; init; }

  /// <summary>
  /// The slugs the tag used to have, which redirect to the current one. When writing, the previous slugs to keep: one
  /// left out is dropped, which frees it for any tag to use, and one the tag does not have is ignored.
  /// </summary>
  public required List<string> PreviousSlugs { get; init; }
}

/// <summary>What a client sends to create a tag or to replace one.</summary>
[ValidatableType]
public sealed record TagRequest : TagBase {
  /// <summary>
  /// The Version the client last read. When another save has changed the tag since, the replace is refused with 409,
  /// so one person's edit does not silently undo another's. Null skips the check (an overwrite), and it is ignored when
  /// creating.
  /// </summary>
  public required int? ExpectedVersion { get; init; }
}

/// <summary>A tag as the API returns it.</summary>
public sealed record TagResponse : TagBase {
  /// <summary>The tag's ID, which its URL names.</summary>
  public required int TagId { get; init; }

  /// <summary>The description rendered to HTML that is safe to show as it is, or null when there is no description.</summary>
  public required string? DescriptionHtml { get; init; }

  /// <summary>How many times the tag has been saved, starting at 1. Send it back as ExpectedVersion to detect a save made in between.</summary>
  public required int Version { get; init; }
}
