using JoshOlson.Api.Common.Validation;

namespace JoshOlson.Api.Tags;

public sealed class TagService {
  private readonly TagRepository _tagRepository;
  private readonly PayloadValidator _validator;

  public TagService(TagRepository tagRepository, PayloadValidator validator) {
    _tagRepository = tagRepository;
    _validator = validator;
  }

  public async Task<ValidationErrors> ValidateCreateAsync(TagRequest request, CancellationToken cancellationToken)
    => await ValidateAsync(null, request, cancellationToken);

  public async Task<ValidationErrors> ValidateUpdateAsync(
    int tagId,
    TagRequest request,
    CancellationToken cancellationToken
  )
    => await ValidateAsync(tagId, request, cancellationToken);

  public Task<IReadOnlyList<TagResponse>> GetAllAsync(CancellationToken cancellationToken)
    => _tagRepository.GetAllAsync(cancellationToken);

  public Task<TagResponse?> GetAsync(int tagId, CancellationToken cancellationToken)
    => _tagRepository.GetAsync(tagId, cancellationToken);

  public Task<int> CreateAsync(TagRequest request, CancellationToken cancellationToken)
    => _tagRepository.CreateAsync(request, cancellationToken);

  public Task<bool> UpdateAsync(int tagId, TagRequest request, CancellationToken cancellationToken)
    => _tagRepository.UpdateAsync(tagId, request, cancellationToken);

  public Task<bool> DeleteAsync(int tagId, CancellationToken cancellationToken)
    => _tagRepository.DeleteAsync(tagId, cancellationToken);

  // The attributes cover the shape of each field. Uniqueness needs the database, and is only checked for a field that
  // is otherwise valid, in one query. The unique constraints stay as the backstop for a concurrent insert.
  private async Task<ValidationErrors> ValidateAsync(int? tagId, TagRequest request, CancellationToken cancellationToken) {
    var errors = await _validator.ValidateAsync(request, cancellationToken);
    var (isSlugTaken, isNameTaken) = await _tagRepository.CheckTakenAsync(
      errors.Has("slug") ? null : request.Slug,
      errors.Has("name") ? null : request.Name,
      tagId,
      cancellationToken);
    if (isSlugTaken) errors.Add("slug", "Slug is already in use.");
    if (isNameTaken) errors.Add("name", "Name is already in use.");
    return errors;
  }
}
