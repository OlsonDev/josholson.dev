using JoshOlson.Api.Common.Deletion;
using Xunit;

namespace JoshOlson.Api.Tags;

public class TagServiceTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly TagService _tagService;
  private readonly TagRepository _tagRepository;
  private readonly TagTestData _tagTestData;

  public TagServiceTests(TagService tagService, TagRepository tagRepository, TagTestData tagTestData) {
    _tagService = tagService;
    _tagRepository = tagRepository;
    _tagTestData = tagTestData;
  }

  private static TagRequest NewRequest() {
    var slug = $"test-{Guid.NewGuid():N}";
    return new() {
      Name = slug,
      Slug = slug,
      Description = null,
      Color = "emerald-200",
      Aliases = [],
      PreviousSlugs = [],
      ExpectedVersion = null,
    };
  }

  [Fact]
  public async Task ValidateCreateAsync_ReportsEveryFieldError() {
    var request = new TagRequest {
      Name = " foo",
      Slug = "Not Kebab",
      Description = "",
      Color = "red",
      Aliases = ["", "bar", "BAR"],
      PreviousSlugs = ["Not Kebab"],
      ExpectedVersion = null,
    };

    var errors = await _tagService.ValidateCreateAsync(request, _cancellationToken);

    var fields = errors.ToProblemDetails().Errors.Keys.Order(StringComparer.Ordinal);
    Assert.Equal(
      ["aliases[0]", "aliases[2]", "color", "description", "name", "previousSlugs[0]", "slug"],
      fields);
  }

  [Fact]
  public async Task ValidateCreateAsync_TakenNameAndSlug_AreReported() {
    var request = NewRequest();
    var tagId = await _tagService.CreateAsync(request, _cancellationToken);
    try {
      var duplicate = request with { Name = request.Name.ToUpperInvariant() };
      var errors = await _tagService.ValidateCreateAsync(duplicate, _cancellationToken);

      Assert.Equal(["name", "slug"], errors.ToProblemDetails().Errors.Keys.Order(StringComparer.Ordinal));
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task ValidateCreateAsync_AliasThatIsAnotherTagsNameOrAlias_IsReported() {
    var first = NewRequest() with { Aliases = [$"alias-{Guid.NewGuid():N}"] };
    var firstId = await _tagService.CreateAsync(first, _cancellationToken);
    try {
      var second = NewRequest() with { Aliases = [first.Name.ToUpperInvariant(), first.Aliases[0], "free-" + Guid.NewGuid().ToString("N")] };

      var errors = await _tagService.ValidateCreateAsync(second, _cancellationToken);

      Assert.Equal(["aliases[0]", "aliases[1]"], errors.ToProblemDetails().Errors.Keys.Order(StringComparer.Ordinal));
    } finally {
      await _tagRepository.DeleteAsync(firstId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task ValidateUpdateAsync_OwnNameSlugAndAliases_AreNotTaken() {
    var request = NewRequest() with { Aliases = [$"alias-{Guid.NewGuid():N}"] };
    var tagId = await _tagService.CreateAsync(request, _cancellationToken);
    try {
      var own = await _tagService.ValidateUpdateAsync(tagId, request with { Description = "foo" }, _cancellationToken);
      var other = await _tagService.ValidateUpdateAsync(tagId + 1, request, _cancellationToken);

      Assert.False(own.Any);
      Assert.True(other.Has("slug"));
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task CreateAsync_RendersTheDescriptionAndStoresAliases() {
    var request = NewRequest() with {
      Description = "A **foo**",
      Aliases = ["zzz-" + Guid.NewGuid().ToString("N")],
    };
    var tagId = await _tagService.CreateAsync(request, _cancellationToken);
    try {
      var tag = await _tagService.GetAsync(tagId, _cancellationToken);

      Assert.Equal("<p>A <strong>foo</strong></p>", tag?.DescriptionHtml);
      Assert.Equal(request.Aliases, tag?.Aliases);
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task UpdateAsync_NewSlug_KeepsTheOldOneAndFindsBoth() {
    var request = NewRequest();
    var tagId = await _tagService.CreateAsync(request, _cancellationToken);
    try {
      var renamed = request with { Slug = $"test-{Guid.NewGuid():N}" };

      Assert.IsType<TagUpdated>((await _tagService.UpdateAsync(tagId, renamed, _cancellationToken)).Value);

      var tag = await _tagService.GetAsync(tagId, _cancellationToken);
      Assert.Equal(renamed.Slug, tag?.Slug);
      Assert.Equal([request.Slug], tag?.PreviousSlugs);
      Assert.Equal(renamed.Slug, Assert.IsType<TagResponse>((await _tagService.GetBySlugAsync(renamed.Slug, _cancellationToken)).Value).Slug);
      Assert.Equal(renamed.Slug, Assert.IsType<TagMoved>((await _tagService.GetBySlugAsync(request.Slug, _cancellationToken)).Value).Slug);
      Assert.IsType<TagNotFound>((await _tagService.GetBySlugAsync($"test-{Guid.NewGuid():N}", _cancellationToken)).Value);

      // Dropping the previous slug frees it. Restoring an earlier one needs no new row.
      var dropped = renamed with { PreviousSlugs = [] };
      Assert.IsType<TagUpdated>((await _tagService.UpdateAsync(tagId, dropped, _cancellationToken)).Value);
      Assert.Empty((await _tagService.GetAsync(tagId, _cancellationToken))!.PreviousSlugs);
      Assert.IsType<TagNotFound>((await _tagService.GetBySlugAsync(request.Slug, _cancellationToken)).Value);
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task UpdateAsync_PreviousSlugAsSlug_RestoresIt() {
    var request = NewRequest();
    var tagId = await _tagService.CreateAsync(request, _cancellationToken);
    try {
      var renamed = request with { Slug = $"test-{Guid.NewGuid():N}" };
      await _tagService.UpdateAsync(tagId, renamed, _cancellationToken);

      var restored = renamed with { Slug = request.Slug, PreviousSlugs = [renamed.Slug] };
      Assert.IsType<TagUpdated>((await _tagService.UpdateAsync(tagId, restored, _cancellationToken)).Value);

      var tag = await _tagService.GetAsync(tagId, _cancellationToken);
      Assert.Equal(request.Slug, tag?.Slug);
      Assert.Equal([renamed.Slug], tag?.PreviousSlugs);
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task UpdateAsync_StaleVersion_IsRefusedAndMissingTagIsNotFound() {
    var request = NewRequest();
    var tagId = await _tagService.CreateAsync(request, _cancellationToken);
    try {
      var read = await _tagService.GetAsync(tagId, _cancellationToken);
      var first = request with { Description = "foo", ExpectedVersion = read!.Version };

      Assert.IsType<TagUpdated>((await _tagService.UpdateAsync(tagId, first, _cancellationToken)).Value);
      Assert.IsType<TagChanged>((await _tagService.UpdateAsync(tagId, first, _cancellationToken)).Value);
      // Overwriting skips the check.
      var overwrite = first with { ExpectedVersion = null };
      Assert.IsType<TagUpdated>((await _tagService.UpdateAsync(tagId, overwrite, _cancellationToken)).Value);
    } finally {
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }

    Assert.IsType<TagNotFound>((await _tagService.UpdateAsync(tagId, request, _cancellationToken)).Value);
  }

  [Fact]
  public async Task DeletionImpact_NothingReferencesTheTag_IsEmptyAndDeleteGoesAhead() {
    var tagId = await _tagService.CreateAsync(NewRequest(), _cancellationToken);

    var impact = await _tagService.GetDeletionImpactAsync(tagId, _cancellationToken);
    Assert.NotNull(impact);
    Assert.Empty(impact.Blockers);
    Assert.Empty(impact.Warnings);

    Assert.IsType<TagDeleted>((await _tagService.DeleteAsync(tagId, _cancellationToken)).Value);
    Assert.Null(await _tagService.GetDeletionImpactAsync(tagId, _cancellationToken));
    Assert.IsType<TagNotFound>((await _tagService.DeleteAsync(tagId, _cancellationToken)).Value);
  }

  [Fact]
  public async Task DeletionImpact_ReferencesAreWarnings_NeverBlockers() {
    var tagId = await _tagService.CreateAsync(NewRequest(), _cancellationToken);
    var postId = await _tagTestData.CreatePostWithTagAsync(tagId, _cancellationToken);
    try {
      var impact = await _tagService.GetDeletionImpactAsync(tagId, _cancellationToken);

      Assert.Empty(impact!.Blockers);
      Assert.Equal([DeletionImpactKind.Post], impact.Warnings.Select(w => w.Kind));
      Assert.Equal([1], impact.Warnings.Select(w => w.Count));
      Assert.IsType<TagDeleted>((await _tagService.DeleteAsync(tagId, _cancellationToken)).Value);
    } finally {
      await _tagTestData.DeletePostAsync(postId);
      await _tagRepository.DeleteAsync(tagId, CancellationToken.None);
    }
  }
}
