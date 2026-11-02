using System.Net;
using System.Text;
using JoshOlson.Api.Auth;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace JoshOlson.Api.Tags;

// Creating, changing, and deleting tags needs a session, so every test runs signed in as a throwaway user. Whether
// each endpoint demands one is `SessionFilterTests`' question.
public class TagControllerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly HttpClient _client;
  private readonly AuthTestData _authTestData;
  private int _userId;

  public TagControllerTests(HttpClient client, AuthTestData authTestData) {
    _client = client;
    _authTestData = authTestData;
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
      Color = null,
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
      Assert.Contains(tags!, t => t == tag);

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
}
