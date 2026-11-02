using Xunit;

namespace JoshOlson.Api.Tags;

public class TagServiceTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly TagService _tagService;

  public TagServiceTests(TagService tagService)
    => _tagService = tagService;

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
  public async Task ValidateCreateAsync_ReportsEveryFieldError() {
    var request = new TagRequest {
      Name = " foo",
      Slug = "Not Kebab",
      Description = "",
      Color = "red",
    };

    var errors = await _tagService.ValidateCreateAsync(request, _cancellationToken);

    var fields = errors.ToProblemDetails().Errors.Keys.Order(StringComparer.Ordinal);
    Assert.Equal(["color", "description", "name", "slug"], fields);
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
      await _tagService.DeleteAsync(tagId, CancellationToken.None);
    }
  }

  [Fact]
  public async Task ValidateUpdateAsync_OwnNameAndSlug_AreNotTaken() {
    var request = NewRequest();
    var tagId = await _tagService.CreateAsync(request, _cancellationToken);
    try {
      var own = await _tagService.ValidateUpdateAsync(tagId, request with { Description = "foo" }, _cancellationToken);
      var other = await _tagService.ValidateUpdateAsync(tagId + 1, request, _cancellationToken);

      Assert.False(own.Any);
      Assert.True(other.Has("slug"));
    } finally {
      await _tagService.DeleteAsync(tagId, CancellationToken.None);
    }
  }
}
