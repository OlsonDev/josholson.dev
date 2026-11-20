using JoshOlson.Api.Common.Deletion;

namespace JoshOlson.Api.Tags;

// Which of a tag's unique fields another tag already has. A field that was not checked is not taken.
public sealed record TakenTagFields(bool IsSlugTaken, bool IsNameTaken);

// How many of each kind of thing is tagged with a tag.
public sealed record TagReferenceCounts(int Works, int Projects, int Skills, int Posts);

// What replacing a tag comes to. The controller gives each case a status code.
public readonly union UpdateResult(TagUpdated, TagNotFound, TagChanged);

// What asking for a tag by one of its slugs comes to: the tag, nothing, or the tag's current slug when the one asked
// for is a previous slug (a 308 to it).
public readonly union SlugLookupResult(TagResponse, TagNotFound, TagMoved);

// What deleting a tag comes to.
public readonly union DeleteResult(TagDeleted, TagNotFound, TagBlocked);

public sealed record TagUpdated;

// There is no tag with that ID.
public sealed record TagNotFound;

// The tag was saved by someone else after the client read it.
public sealed record TagChanged;

// The slug asked for is a previous slug, and the tag now has this one.
public sealed record TagMoved(string Slug);

public sealed record TagDeleted;

// Something refers to the tag in a way that forbids deleting it.
public sealed record TagBlocked(DeletionImpact Impact);
