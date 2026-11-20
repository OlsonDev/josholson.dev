namespace JoshOlson.Api.Common.Deletion;

/// <summary>A kind of thing that refers to the item about to be deleted.</summary>
public enum DeletionImpactKind : short {
  /// <summary>A job on a résumé.</summary>
  Work = 1,

  /// <summary>A project on a résumé.</summary>
  Project = 2,

  /// <summary>A skill on a résumé.</summary>
  Skill = 3,

  /// <summary>A blog post.</summary>
  Post = 4,
}

/// <summary>How many things of one kind refer to the item.</summary>
public sealed record DeletionImpactItem {
  /// <summary>What kind of thing it is.</summary>
  public required DeletionImpactKind Kind { get; init; }

  /// <summary>How many of them there are. Always at least 1.</summary>
  public required int Count { get; init; }
}

/// <summary>
/// What deleting an item would affect, which the web's delete confirmation shows (`ConfirmDelete`, in
/// `web/src/lib/admin`). Blockers make the delete impossible: it is refused with this body. Warnings are consequences
/// the user should know about and accept, such as a tag being taken off every skill that uses it. Used by: tags.
/// </summary>
public sealed record DeletionImpact {
  /// <summary>What prevents the delete. Empty when it can go ahead.</summary>
  public required List<DeletionImpactItem> Blockers { get; init; }

  /// <summary>What the delete would also change or remove.</summary>
  public required List<DeletionImpactItem> Warnings { get; init; }
}
