using System.Net;
using JoshOlson.Api.Common.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace JoshOlson.Api.Tags;

public class TagControllerTests {
  private static WebApplicationFactory<Program> CreateFactory()
    => new WebApplicationFactory<Program>().WithWebHostBuilder(
      builder => builder.UseSetting("ConnectionStrings:Default", TestDatabase.ConnectionString));

  private static TagRequest NewRequest(string? slug = null) {
    slug ??= $"test-{Guid.NewGuid():N}";
    return new() { Name = slug, Slug = slug, Description = null, Color = null };
  }

  // Returns the new tag's URL from the Location header.
  private static async Task<string> CreateTagAsync(HttpClient client, TagRequest request, CancellationToken cancellationToken) {
    var response = await client.PostAsJsonAsync("/tags", request, cancellationToken);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return response.Headers.Location?.AbsolutePath ?? throw new InvalidOperationException("No Location header.");
  }

  [Fact]
  public async Task Crud_RoundTrips() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var factory = CreateFactory();
    using var client = factory.CreateClient();
    var request = NewRequest();

    var url = await CreateTagAsync(client, request, cancellationToken);
    try {
      var tag = await client.GetFromJsonAsync<TagResponse>(url, cancellationToken);
      Assert.Equal(request.Slug, tag?.Slug);
      var tags = await client.GetFromJsonAsync<List<TagResponse>>("/tags", cancellationToken);
      Assert.Contains(tags!, t => t == tag);

      var updateResponse = await client.PutAsJsonAsync(url, request with { Description = "foo" }, cancellationToken);
      Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
      Assert.Equal("foo", (await client.GetFromJsonAsync<TagResponse>(url, cancellationToken))?.Description);
    } finally {
      Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url, CancellationToken.None)).StatusCode);
    }

    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url, cancellationToken)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(url, cancellationToken)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(url, request, cancellationToken)).StatusCode);
  }

  [Fact]
  public async Task Create_DuplicateSlug_ReturnsUnprocessable() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var factory = CreateFactory();
    using var client = factory.CreateClient();
    var request = NewRequest();
    var url = await CreateTagAsync(client, request, cancellationToken);
    try {
      var duplicate = await client.PostAsJsonAsync("/tags", request, cancellationToken);

      Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
      var details = await duplicate.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
      Assert.Equal(["name", "slug"], details!.Errors.Keys.Order(StringComparer.Ordinal));
    } finally {
      await client.DeleteAsync(url, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Create_InvalidFields_ReturnsEveryError() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var factory = CreateFactory();
    using var client = factory.CreateClient();
    var request = new TagRequest { Name = " foo", Slug = "Not Kebab Case", Description = null, Color = "red" };

    var response = await client.PostAsJsonAsync("/tags", request, cancellationToken);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
    Assert.Equal(["color", "name", "slug"], details!.Errors.Keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task Update_SlugOfAnotherTag_ReturnsUnprocessable() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var factory = CreateFactory();
    using var client = factory.CreateClient();
    var first = NewRequest();
    var firstUrl = await CreateTagAsync(client, first, cancellationToken);
    var secondUrl = await CreateTagAsync(client, NewRequest(), cancellationToken);
    try {
      var response = await client.PutAsJsonAsync(secondUrl, NewRequest(first.Slug), cancellationToken);

      Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    } finally {
      await client.DeleteAsync(firstUrl, CancellationToken.None);
      await client.DeleteAsync(secondUrl, CancellationToken.None);
    }
  }

  [Fact]
  public async Task Create_MalformedJson_ReturnsBadRequest() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var factory = CreateFactory();
    using var client = factory.CreateClient();
    using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

    var response = await client.PostAsync("/tags", content, cancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
