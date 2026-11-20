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
      Aliases = [],
      PreviousSlugs = [],
      ExpectedVersion = null,
    };
  }

  // The tag and its current slug, which is how the service always makes one.
  private async Task<int> CreateAsync(TagRequest request) {
    var tagId = await _tagRepository.CreateAsync(request, descriptionHtml: null, _cancellationToken);
    await _tagRepository.AddSlugAsync(tagId, request.Slug, isCanonical: true, _cancellationToken);
    return tagId;
  }

  [Fact]
  public async Task Lifecycle_CreatesReadsUpdatesAndDeletes() {
    var request = NewRequest();

    var tagId = await CreateAsync(request);
    try {
      var created = await _tagRepository.GetAsync(tagId, _cancellationToken);
      Assert.NotNull(created);
      Assert.Equal(request.Slug, created.Slug);
      Assert.Null(created.Description);
      Assert.Null(created.DescriptionHtml);
      Assert.Empty(created.Aliases);
      Assert.Empty(created.PreviousSlugs);
      Assert.Contains(await _tagRepository.GetAllAsync(_cancellationToken), t => t.TagId == tagId);

      var changed = request with { Description = "foo", Color = "emerald-200" };
      Assert.True(await _tagRepository.UpdateAsync(tagId, changed, "<p>foo</p>", _cancellationToken));
      var updated = await _tagRepository.GetAsync(tagId, _cancellationToken);
      Assert.Equal("foo", updated?.Description);
      Assert.Equal("<p>foo</p>", updated?.DescriptionHtml);
      Assert.Equal("emerald-200", updated?.Color);
    } finally {
      Assert.True(await _tagRepository.DeleteAsync(tagId, CancellationToken.None));
    }

    Assert.Null(await _tagRepository.GetAsync(tagId, _cancellationToken));
    Assert.False(await _tagRepository.UpdateAsync(tagId, request, descriptionHtml: null, _cancellationToken));
    Assert.False(await _tagRepository.DeleteAsync(tagId, _cancellationToken));
  }

  [Fact]
  public async Task UpdateAsync_ExpectedVersion_OnlyMatchesTheCurrentOne() {
    var request = NewRequest();
    var tagId = await CreateAsync(request);
    try {
      var created = await _tagRepository.GetAsync(tagId, _cancellationToken);
      var first = request with { Description = "foo", ExpectedVersion = created!.Version };

      Assert.True(await _tagRepository.UpdateAsync(tagId, first, "<p>foo</p>", _cancellationToken));
      // The same version again is stale now, because the first save moved the version on.
      Assert.False(await _tagRepository.UpdateAsync(tagId, first, "<p>foo</p>", _cancellationToken));
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Slugs_CurrentAndPrevious_AreFoundAndKeptApart() {
    var request = NewRequest();
    var tagId = await CreateAsync(request);
    try {
      var next = $"test-{Guid.NewGuid():N}";
      await _tagRepository.AddSlugAsync(tagId, next, isCanonical: false, _cancellationToken);
      await _tagRepository.SetCanonicalSlugAsync(tagId, next, _cancellationToken);

      var tag = await _tagRepository.GetAsync(tagId, _cancellationToken);
      Assert.Equal(next, tag?.Slug);
      Assert.Equal([request.Slug], tag?.PreviousSlugs);
      Assert.Equal(tagId, await _tagRepository.GetTagIdBySlugAsync(request.Slug, _cancellationToken));
      Assert.Equal(tagId, await _tagRepository.GetTagIdBySlugAsync(next, _cancellationToken));
      Assert.Null(await _tagRepository.GetTagIdBySlugAsync($"test-{Guid.NewGuid():N}", _cancellationToken));
      // A previous slug still counts as taken, for any other tag.
      Assert.True((await _tagRepository.CheckTakenAsync(request.Slug, nameToCheck: null, exceptTagId: null, _cancellationToken)).IsSlugTaken);

      await _tagRepository.DeleteSlugsExceptAsync(tagId, [], _cancellationToken);
      Assert.Empty((await _tagRepository.GetAsync(tagId, _cancellationToken))!.PreviousSlugs);
      Assert.Null(await _tagRepository.GetTagIdBySlugAsync(request.Slug, _cancellationToken));
      Assert.Equal(next, (await _tagRepository.GetAsync(tagId, _cancellationToken))?.Slug);
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Aliases_AreReadInOrderAndReplaced() {
    var tagId = await CreateAsync(NewRequest());
    try {
      var suffix = Guid.NewGuid().ToString("N");
      await _tagRepository.AddAliasAsync(tagId, $"zzz-{suffix}", _cancellationToken);
      await _tagRepository.AddAliasAsync(tagId, $"AAA-{suffix}", _cancellationToken);

      Assert.Equal([$"AAA-{suffix}", $"zzz-{suffix}"], (await _tagRepository.GetAsync(tagId, _cancellationToken))?.Aliases);

      await _tagRepository.DeleteAliasesAsync(tagId, _cancellationToken);
      Assert.Empty((await _tagRepository.GetAsync(tagId, _cancellationToken))!.Aliases);
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task GetTakenAliasesAsync_FindsOtherTagsNamesAndAliases_AndSkipsTheTagItself() {
    var first = NewRequest();
    var firstId = await CreateAsync(first);
    var secondId = await CreateAsync(NewRequest());
    try {
      var alias = $"alias-{Guid.NewGuid():N}";
      await _tagRepository.AddAliasAsync(firstId, alias, _cancellationToken);
      var free = $"free-{Guid.NewGuid():N}";
      List<string> names = [
        alias,
        first.Name.ToLowerInvariant(),
        free,
      ];

      var seenBySecond = await _tagRepository.GetTakenAliasesAsync(names, secondId, _cancellationToken);
      var seenByFirst = await _tagRepository.GetTakenAliasesAsync(names, firstId, _cancellationToken);
      var seenByNew = await _tagRepository.GetTakenAliasesAsync(names, exceptTagId: null, _cancellationToken);

      Assert.Equal([alias, first.Name], seenBySecond.Order(StringComparer.Ordinal));
      Assert.Empty(seenByFirst);
      Assert.Equal([alias, first.Name], seenByNew.Order(StringComparer.Ordinal));
    } finally {
      await _tagRepository.DeleteAsync(firstId, CancellationToken.None);
      await _tagRepository.DeleteAsync(secondId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task GetReferenceCountsAsync_NoReferences_IsZeroAndMissingTagIsNull() {
    var tagId = await CreateAsync(NewRequest());
    try {
      Assert.Equal(new TagReferenceCounts(Works: 0, Projects: 0, Skills: 0, Posts: 0), await _tagRepository.GetReferenceCountsAsync(tagId, _cancellationToken));
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }

    Assert.Null(await _tagRepository.GetReferenceCountsAsync(tagId, _cancellationToken));
  }

  [Fact]
  public async Task CheckTakenAsync_ReportsEachFieldIndependently() {
    var request = NewRequest();
    var tagId = await CreateAsync(request);
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
      tagId = await CreateAsync(NewRequest());
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
      tagId = await CreateAsync(NewRequest());
      await transaction.CommitAsync(_cancellationToken);
    }

    try {
      Assert.NotNull(await _tagRepository.GetAsync(tagId, _cancellationToken));
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  private async Task<(bool Slug, bool Name)> Taken(string? slug, string? name, int? excludeTagId) {
    var (isSlugTaken, isNameTaken) = await _tagRepository.CheckTakenAsync(slug, name, excludeTagId, _cancellationToken);
    return (isSlugTaken, isNameTaken);
  }
}
