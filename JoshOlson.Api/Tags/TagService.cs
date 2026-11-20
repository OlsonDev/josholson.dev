using System.Globalization;
using JoshOlson.Api.Common.Deletion;
using JoshOlson.Api.Common.Markdown;
using JoshOlson.Api.Common.Validation;
using Npgsql;

namespace JoshOlson.Api.Tags;

public sealed class TagService {
  private readonly NpgsqlConnection _connection;
  private readonly TagRepository _tagRepository;
  private readonly PayloadValidator _validator;
  private readonly MarkdownService _markdownService;

  public TagService(
    NpgsqlConnection connection,
    TagRepository tagRepository,
    PayloadValidator validator,
    MarkdownService markdownService
  ) {
    _connection = connection;
    _tagRepository = tagRepository;
    _validator = validator;
    _markdownService = markdownService;
  }

  public async Task<ValidationErrors> ValidateCreateAsync(TagRequest request, CancellationToken cancellationToken)
    => await ValidateAsync(tagId: null, request, cancellationToken);

  public async Task<ValidationErrors> ValidateUpdateAsync(
    int tagId,
    TagRequest request,
    CancellationToken cancellationToken
  )
    => await ValidateAsync(tagId, request, cancellationToken);

  public Task<List<TagResponse>> GetAllAsync(CancellationToken cancellationToken)
    => _tagRepository.GetAllAsync(cancellationToken);

  public Task<TagResponse?> GetAsync(int tagId, CancellationToken cancellationToken)
    => _tagRepository.GetAsync(tagId, cancellationToken);

  public async Task<SlugLookupResult> GetBySlugAsync(string slug, CancellationToken cancellationToken) {
    var tagId = await _tagRepository.GetTagIdBySlugAsync(slug, cancellationToken);
    var tag = tagId is { } id ? await _tagRepository.GetAsync(id, cancellationToken) : null;
    return tag is null
      ? new TagNotFound()
      : string.Equals(tag.Slug, slug, StringComparison.Ordinal)
        ? tag
        : new TagMoved(tag.Slug);
  }

  public async Task<int> CreateAsync(TagRequest request, CancellationToken cancellationToken) {
    await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
    var descriptionHtml = _markdownService.ToHtml(request.Description);
    var tagId = await _tagRepository.CreateAsync(request, descriptionHtml, cancellationToken);
    await _tagRepository.AddSlugAsync(tagId, request.Slug, isCanonical: true, cancellationToken);
    await ReplaceAliasesAsync(tagId, request.Aliases, cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return tagId;
  }

  public async Task<UpdateResult> UpdateAsync(int tagId, TagRequest request, CancellationToken cancellationToken) {
    await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
    var current = await _tagRepository.GetAsync(tagId, cancellationToken);
    if (current is null) return new TagNotFound();

    var descriptionHtml = _markdownService.ToHtml(request.Description);
    if (!await _tagRepository.UpdateAsync(tagId, request, descriptionHtml, cancellationToken)) return new TagChanged();

    // A new slug becomes the current one, and the old one stays as a previous slug, so its URL keeps working. A slug
    // the tag used to have comes back without being added again.
    if (!string.Equals(request.Slug, current.Slug, StringComparison.Ordinal)) {
      if (!current.PreviousSlugs.Contains(request.Slug))
        await _tagRepository.AddSlugAsync(tagId, request.Slug, isCanonical: false, cancellationToken);
      await _tagRepository.SetCanonicalSlugAsync(tagId, request.Slug, cancellationToken);
    }
    List<string> slugsToKeep = [
      .. request.PreviousSlugs,
      current.Slug,
    ];
    await _tagRepository.DeleteSlugsExceptAsync(tagId, slugsToKeep, cancellationToken);
    await ReplaceAliasesAsync(tagId, request.Aliases, cancellationToken);

    await transaction.CommitAsync(cancellationToken);
    return new TagUpdated();
  }

  // Null when there is no such tag.
  public async Task<DeletionImpact?> GetDeletionImpactAsync(int tagId, CancellationToken cancellationToken) {
    var counts = await _tagRepository.GetReferenceCountsAsync(tagId, cancellationToken);
    return counts is null ? null : BuildImpact(counts);
  }

  // Checks again in the transaction that deletes, so a tag that became blocked after the user saw the dialog is not
  // deleted. Nothing blocks a tag today: a tag on a skill, a project, a job, or a post goes off them, which is a
  // warning, so the blocked case is for the day something makes a tag too important to delete.
  public async Task<DeleteResult> DeleteAsync(int tagId, CancellationToken cancellationToken) {
    await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
    var impact = await GetDeletionImpactAsync(tagId, cancellationToken);
    if (impact is null) return new TagNotFound();
    if (impact.Blockers.Count > 0) return new TagBlocked(impact);

    var isDeleted = await _tagRepository.DeleteAsync(tagId, cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return isDeleted ? new TagDeleted() : new TagNotFound();
  }

  private static DeletionImpact BuildImpact(TagReferenceCounts counts) {
    var warnings = new List<DeletionImpactItem>(4);
    AddIfAny(warnings, DeletionImpactKind.Work, counts.Works);
    AddIfAny(warnings, DeletionImpactKind.Project, counts.Projects);
    AddIfAny(warnings, DeletionImpactKind.Skill, counts.Skills);
    AddIfAny(warnings, DeletionImpactKind.Post, counts.Posts);
    return new() {
      Blockers = [],
      Warnings = warnings,
    };
  }

  private static void AddIfAny(List<DeletionImpactItem> items, DeletionImpactKind kind, int count) {
    if (count > 0) {
      items.Add(new() {
        Kind = kind,
        Count = count,
      });
    }
  }

  private async Task ReplaceAliasesAsync(int tagId, List<string> aliases, CancellationToken cancellationToken) {
    await _tagRepository.DeleteAliasesAsync(tagId, cancellationToken);
    foreach (var alias in aliases) await _tagRepository.AddAliasAsync(tagId, alias, cancellationToken);
  }

  // The attributes cover the shape of each field. The items of the two lists are checked here, because an attribute
  // on a list sees the list. Uniqueness needs the database, and is only checked for a field that is otherwise valid,
  // in one query. The unique constraints stay as the backstop for a concurrent insert.
  private async Task<ValidationErrors> ValidateAsync(int? tagId, TagRequest request, CancellationToken cancellationToken) {
    var errors = await _validator.ValidateAsync(request, cancellationToken);
    ValidateAliasShapes(request, errors);
    ValidatePreviousSlugShapes(request, errors);

    var (isSlugTaken, isNameTaken) = await _tagRepository.CheckTakenAsync(
      errors.Has("slug") ? null : request.Slug,
      errors.Has("name") ? null : request.Name,
      tagId,
      cancellationToken);
    if (isSlugTaken) errors.Add("slug", "Slug is already in use.");
    if (isNameTaken) errors.Add("name", "Name is already in use.");

    await ValidateAliasesAreFreeAsync(tagId, request, errors, cancellationToken);
    return errors;
  }

  // The name of one item of a list in the request, as the errors key it.
  private static string Item(string list, int index)
    => string.Create(CultureInfo.InvariantCulture, $"{list}[{index}]");

  private static void ValidateAliasShapes(TagRequest request, ValidationErrors errors) {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < request.Aliases.Count; i++) {
      var alias = request.Aliases[i];
      var field = Item("aliases", i);
      if (string.IsNullOrEmpty(alias) || !string.Equals(alias, alias.Trim(), StringComparison.Ordinal)) {
        errors.Add(field, "Alias must not be empty or have leading or trailing whitespace.");
      } else if (!seen.Add(alias)) {
        errors.Add(field, "Alias is listed twice.");
      } else if (alias.Equals(request.Name, StringComparison.OrdinalIgnoreCase)) {
        errors.Add(field, "Alias repeats the tag's name.");
      }
    }
  }

  private static void ValidatePreviousSlugShapes(TagRequest request, ValidationErrors errors) {
    var kebabCase = new KebabCaseAttribute();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 0; i < request.PreviousSlugs.Count; i++) {
      var slug = request.PreviousSlugs[i];
      var field = Item("previousSlugs", i);
      if (string.IsNullOrEmpty(slug) || !kebabCase.IsValid(slug)) {
        errors.Add(field, kebabCase.FormatErrorMessage("Previous slug"));
      } else if (!seen.Add(slug)) {
        errors.Add(field, "Previous slug is listed twice.");
      }
    }
  }

  // An alias that is no problem on its own is checked against every other tag's names and aliases in one query.
  private async Task ValidateAliasesAreFreeAsync(
    int? tagId,
    TagRequest request,
    ValidationErrors errors,
    CancellationToken cancellationToken
  ) {
    var lowerNames = new List<string>(request.Aliases.Count);
    for (var i = 0; i < request.Aliases.Count; i++)
      if (!errors.Has(Item("aliases", i))) lowerNames.Add(request.Aliases[i].ToLowerInvariant());
    if (lowerNames.Count is 0) return;

    var taken = await _tagRepository.GetTakenAliasesAsync(lowerNames, tagId, cancellationToken);
    if (taken.Count is 0) return;

    var takenSet = new HashSet<string>(taken, StringComparer.Ordinal);
    for (var i = 0; i < request.Aliases.Count; i++)
      if (!errors.Has(Item("aliases", i)) && takenSet.Contains(request.Aliases[i].ToLowerInvariant()))
        errors.Add(Item("aliases", i), "Alias is already the name or alias of another tag.");
  }
}
