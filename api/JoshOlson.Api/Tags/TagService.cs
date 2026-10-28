namespace JoshOlson.Api.Tags;

public sealed class TagService {
  private readonly TagRepository _repository;

  public TagService(TagRepository repository)
    => _repository = repository;

  public Task<IReadOnlyList<TagResponse>> GetAllAsync(CancellationToken cancellationToken)
    => _repository.GetAllAsync(cancellationToken);

  public Task<TagResponse?> GetAsync(int tagId, CancellationToken cancellationToken)
    => _repository.GetAsync(tagId, cancellationToken);

  public Task<int> CreateAsync(TagRequest request, CancellationToken cancellationToken)
    => _repository.CreateAsync(request, cancellationToken);

  public Task<bool> UpdateAsync(int tagId, TagRequest request, CancellationToken cancellationToken)
    => _repository.UpdateAsync(tagId, request, cancellationToken);

  public Task<bool> DeleteAsync(int tagId, CancellationToken cancellationToken)
    => _repository.DeleteAsync(tagId, cancellationToken);
}
