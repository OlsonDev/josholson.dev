using System.Net;
using System.Text;
using JoshOlson.Api.Auth;
using JoshOlson.Api.Common.Deletion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace JoshOlson.Api.Tags;

// Creating, changing, and deleting tags needs a session, so every test runs signed in as a throwaway user. Whether
// each endpoint demands one is `SessionFilterTests`' question.
public class TagControllerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly HttpClient _client;
  private readonly WebApplicationFactory<Program> _factory;
  private readonly AuthTestData _authTestData;
  private readonly TagTestData _tagTestData;
  private int _userId;

  public TagControllerTests(
    HttpClient client,
    WebApplicationFactory<Program> factory,
    AuthTestData authTestData,
    TagTestData tagTestData
  ) {
    _client = client;
    _factory = factory;
    _authTestData = authTestData;
    _tagTestData = tagTestData;
  }

  public async ValueTask InitializeAsync()
    => _userId = await _authTestData.SignInAsync(_client, _cancellationToken);

  public async ValueTask DisposeAsync()
    => await _authTestData.DeleteUserAsync(_userId);

  private static TagRequest NewRequest(string? slug = null) {
    slug ??= $"test-{Guid.NewGuid():N}";
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

  // Returns the new tag's URL from the Location header.
  private async Task<string> CreateTagAsync(TagRequest request) {
    var response = await _client.PostAsJsonAsync("/tags", request, _cancellationToken);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return response.Headers.Location?.AbsolutePath ?? throw new InvalidOperationException("No Location header.");
  }

  [Fact]
  public async Task Crud_RoundTrips() {
    var request = NewRequest();

    var url = await CreateTagAsync(request);
    try {
      var tag = await _client.GetFromJsonAsync<TagResponse>(url, _cancellationToken);
      Assert.Equal(request.Slug, tag?.Slug);
      var tags = await _client.GetFromJsonAsync<List<TagResponse>>("/tags", _cancellationToken);
      Assert.Contains(tags!, t => t.TagId == tag?.TagId);

      var updateResponse = await _client.PutAsJsonAsync(url, request with { Description = "foo" }, _cancellationToken);
      Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
      Assert.Equal("foo", (await _client.GetFromJsonAsync<TagResponse>(url, _cancellationToken))?.Description);
    } finally {
      Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(url, CancellationToken.None)).StatusCode);
    }

    Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(url, _cancellationToken)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync(url, _cancellationToken)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync(url, request, _cancellationToken)).StatusCode);
  }

  [Fact]
  public async Task Create_DuplicateSlug_ReturnsUnprocessable() {
    var request = NewRequest();
    var url = await CreateTagAsync(request);
    try {
      var duplicate = await _client.PostAsJsonAsync("/tags", request, _cancellationToken);

      Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
      var details = await duplicate.Content.ReadFromJsonAsync<ValidationProblemDetails>(_cancellationToken);
      Assert.Equal(["name", "slug"], details!.Errors.Keys.Order(StringComparer.Ordinal));
    } finally {
      await _client.DeleteAsync(url, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Create_InvalidFields_ReturnsEveryError() {
    var request = new TagRequest {
      Name = "   ",
      Slug = "Not Kebab Case",
      Description = null,
      Color = "red",
      Aliases = [],
      PreviousSlugs = [],
      ExpectedVersion = null,
    };

    var response = await _client.PostAsJsonAsync("/tags", request, _cancellationToken);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(_cancellationToken);
    Assert.Equal(["color", "name", "slug"], details!.Errors.Keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task Create_PaddedAndBlankText_IsStoredTrimmedAndAsNull() {
    var slug = $"test-{Guid.NewGuid():N}";
    var request = NewRequest(slug) with {
      Name = $"  {slug}\n",
      Description = "   ",
    };

    var url = await CreateTagAsync(request);
    try {
      var tag = await _client.GetFromJsonAsync<TagResponse>(url, _cancellationToken);

      Assert.Equal(slug, tag?.Name);
      Assert.Null(tag?.Description);
    } finally {
      await _client.DeleteAsync(url, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Update_SlugOfAnotherTag_ReturnsUnprocessable() {
    var first = NewRequest();
    var firstUrl = await CreateTagAsync(first);
    var secondUrl = await CreateTagAsync(NewRequest());
    try {
      var response = await _client.PutAsJsonAsync(secondUrl, NewRequest(first.Slug), _cancellationToken);

      Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    } finally {
      await _client.DeleteAsync(firstUrl, CancellationToken.None);
      await _client.DeleteAsync(secondUrl, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Create_MalformedJson_ReturnsBadRequest() {
    using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

    var response = await _client.PostAsync("/tags", content, _cancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
  [Fact]
  public async Task Update_NewSlug_RedirectsThePreviousOneWith308() {
    var request = NewRequest();
    var url = await CreateTagAsync(request);
    try {
      var renamed = request with { Slug = $"test-{Guid.NewGuid():N}" };
      Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync(url, renamed, _cancellationToken)).StatusCode);

      using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
      var moved = await browser.GetAsync($"/tags/by-slug/{request.Slug}", _cancellationToken);
      var current = await browser.GetAsync($"/tags/by-slug/{renamed.Slug}", _cancellationToken);
      var unknown = await browser.GetAsync($"/tags/by-slug/test-{Guid.NewGuid():N}", _cancellationToken);

      Assert.Equal(HttpStatusCode.PermanentRedirect, moved.StatusCode);
      Assert.Equal($"/tags/by-slug/{renamed.Slug}", moved.Headers.Location?.OriginalString);
      Assert.Equal(HttpStatusCode.OK, current.StatusCode);
      Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
      // A client that follows the redirect, as a fetch in a browser does, ends on the tag.
      var followed = await _client.GetFromJsonAsync<TagResponse>($"/tags/by-slug/{request.Slug}", _cancellationToken);
      Assert.Equal(renamed.Slug, followed?.Slug);
      Assert.Equal([request.Slug], followed?.PreviousSlugs);
    } finally {
      await _client.DeleteAsync(url, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Update_StaleVersion_ReturnsConflictUntilOverwritten() {
    var request = NewRequest();
    var url = await CreateTagAsync(request);
    try {
      var read = await _client.GetFromJsonAsync<TagResponse>(url, _cancellationToken);
      var first = request with { Description = "foo", ExpectedVersion = read!.Version };

      Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync(url, first, _cancellationToken)).StatusCode);
      Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync(url, first, _cancellationToken)).StatusCode);
      var overwrite = first with { ExpectedVersion = null };
      Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync(url, overwrite, _cancellationToken)).StatusCode);
    } finally {
      await _client.DeleteAsync(url, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Create_NoColor_ReturnsUnprocessable() {
    var request = NewRequest() with { Color = null };

    var response = await _client.PostAsJsonAsync("/tags", request, _cancellationToken);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(_cancellationToken);
    Assert.Equal(["color"], details!.Errors.Keys);
  }

  [Fact]
  public async Task Create_InvalidAliases_ReturnEachItemsError() {
    var request = NewRequest() with { Aliases = ["foo", "FOO", ""] };

    var response = await _client.PostAsJsonAsync("/tags", request, _cancellationToken);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(_cancellationToken);
    Assert.Equal(["aliases[1]", "aliases[2]"], details!.Errors.Keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task DeletionImpact_WarnsAboutReferencesAndDeleteStillGoesAhead() {
    var url = await CreateTagAsync(NewRequest());
    var tagId = (await _client.GetFromJsonAsync<TagResponse>(url, _cancellationToken))!.TagId;
    var postId = await _tagTestData.CreatePostWithTagAsync(tagId, _cancellationToken);
    try {
      var impact = await _client.GetFromJsonAsync<DeletionImpact>($"{url}/deletion-impact", _cancellationToken);

      Assert.Empty(impact!.Blockers);
      var warning = Assert.Single(impact.Warnings);
      Assert.Equal(DeletionImpactKind.Post, warning.Kind);
      Assert.Equal(1, warning.Count);
      Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(url, _cancellationToken)).StatusCode);
    } finally {
      await _tagTestData.DeletePostAsync(postId);
      await _client.DeleteAsync(url, CancellationToken.None);
    }

    Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{url}/deletion-impact", _cancellationToken)).StatusCode);
  }
}
