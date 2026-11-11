using Npgsql;
using Xunit;

namespace JoshOlson.Api.Tags;

public class TagRepositoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly NpgsqlConnection _connection;
  private readonly NpgsqlDataSource _dataSource;
  private readonly TagRepository _tagRepository;

  public TagRepositoryTests(NpgsqlConnection connection, NpgsqlDataSource dataSource, TagRepository tagRepository) {
    _connection = connection;
    _dataSource = dataSource;
    _tagRepository = tagRepository;
  }

  private static TagRequest NewRequest() {
    var slug = $"test-{Guid.NewGuid():N}";
    return new() {
      Name = slug,
      Slug = slug,
      Description = null,
      Color = null,
    };
  }

  [Fact]
  public async Task Lifecycle_CreatesReadsUpdatesAndDeletes() {
    var request = NewRequest();

    var tagId = await _tagRepository.CreateAsync(request, _cancellationToken);
    try {
      var created = await _tagRepository.GetAsync(tagId, _cancellationToken);
      Assert.NotNull(created);
      Assert.Equal(request.Slug, created.Slug);
      Assert.Null(created.Description);
      Assert.Contains(await _tagRepository.GetAllAsync(_cancellationToken), t => t.TagId == tagId);

      Assert.True(await _tagRepository.UpdateAsync(tagId, request with { Description = "foo" }, _cancellationToken));
      Assert.Equal("foo", (await _tagRepository.GetAsync(tagId, _cancellationToken))?.Description);
    } finally {
      Assert.True(await _tagRepository.DeleteAsync(tagId, CancellationToken.None));
    }

    Assert.Null(await _tagRepository.GetAsync(tagId, _cancellationToken));
    Assert.False(await _tagRepository.UpdateAsync(tagId, request, _cancellationToken));
    Assert.False(await _tagRepository.DeleteAsync(tagId, _cancellationToken));
  }

  [Fact]
  public async Task CheckTakenAsync_ReportsEachFieldIndependently() {
    var request = NewRequest();
    var tagId = await _tagRepository.CreateAsync(request, _cancellationToken);
    try {
      var other = $"test-{Guid.NewGuid():N}";

      Assert.Equal((true, true), await Taken(request.Slug, request.Name, excludeTagId: null));
      Assert.Equal((true, false), await Taken(request.Slug, other, excludeTagId: null));
      Assert.Equal((false, true), await Taken(other, request.Name.ToUpperInvariant(), excludeTagId: null));
      Assert.Equal((false, false), await Taken(other, other, excludeTagId: null));
      Assert.Equal((true, false), await Taken(request.Slug, name: null, excludeTagId: null));
      Assert.Equal((false, true), await Taken(slug: null, request.Name, excludeTagId: null));
      Assert.Equal((false, false), await Taken(slug: null, name: null, excludeTagId: null));
      // Excluding the tag itself, for each combination of fields checked.
      Assert.Equal((false, false), await Taken(request.Slug, request.Name, tagId));
      Assert.Equal((false, false), await Taken(request.Slug, name: null, tagId));
      Assert.Equal((false, false), await Taken(slug: null, request.Name, tagId));
      // 0 is an ID like any other, not "none".
      Assert.Equal((true, true), await Taken(request.Slug, request.Name, 0));
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  // Repositories sharing one open connection take part in whatever transaction is open on it,
  // and a transaction that is disposed without CommitAsync rolls back.
  [Fact]
  public async Task UncommittedTransaction_RollsBack() {
    int tagId;

    await using (await _connection.BeginTransactionAsync(_cancellationToken)) {
      tagId = await _tagRepository.CreateAsync(NewRequest(), _cancellationToken);
      Assert.NotNull(await _tagRepository.GetAsync(tagId, _cancellationToken));
      await using var otherConnection = await _dataSource.OpenConnectionAsync(_cancellationToken);
      Assert.Null(await new TagRepository(otherConnection).GetAsync(tagId, _cancellationToken));
    }

    Assert.Null(await _tagRepository.GetAsync(tagId, _cancellationToken));
  }

  [Fact]
  public async Task CommittedTransaction_Persists() {
    int tagId;

    await using (var transaction = await _connection.BeginTransactionAsync(_cancellationToken)) {
      tagId = await _tagRepository.CreateAsync(NewRequest(), _cancellationToken);
      await transaction.CommitAsync(_cancellationToken);
    }

    try {
      Assert.NotNull(await _tagRepository.GetAsync(tagId, _cancellationToken));
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  private async Task<(bool Slug, bool Name)> Taken(string? slug, string? name, int? excludeTagId)
    => await _tagRepository.CheckTakenAsync(slug, name, excludeTagId, _cancellationToken);
}
