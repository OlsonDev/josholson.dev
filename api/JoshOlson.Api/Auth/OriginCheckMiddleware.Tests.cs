using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace JoshOlson.Api.Auth;

public class OriginCheckMiddlewareTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;
  private readonly string _allowedOrigin;

  public OriginCheckMiddlewareTests(
    WebApplicationFactory<Program> factory,
    HttpClient client,
    IOptions<AuthOptions> options
  ) {
    _factory = factory;
    _client = client;
    _allowedOrigin = options.Value.AllowedOrigin;
  }

  private static StringContent Json(string body)
    => new(body, Encoding.UTF8, "application/json");

  [Fact]
  public async Task Post_WithoutOrigin_ReturnsForbidden() {
    using var client = _factory.CreateClient();

    var response = await client.PostAsync("/tags", Json("{}"), _cancellationToken);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Theory]
  [InlineData("https://evil.example")]
  [InlineData("http://localhost:5173/")]
  [InlineData("HTTP://LOCALHOST:5173")]
  [InlineData("null")]
  public async Task Post_WithAnotherOrigin_ReturnsForbidden(string origin) {
    using var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Add(HeaderNames.Origin, origin);

    var response = await client.PostAsync("/tags", Json("{}"), _cancellationToken);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task Post_WithABodyThatIsNotJson_ReturnsUnsupportedMediaType() {
    using var form = new StringContent("name=foo", Encoding.UTF8, "application/x-www-form-urlencoded");

    var response = await _client.PostAsync("/tags", form, _cancellationToken);

    Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
  }

  [Fact]
  public async Task Post_WithTheAllowedOriginAndJson_ReachesTheSessionCheck() {
    var response = await _client.PostAsync("/tags", Json("{}"), _cancellationToken);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Get_WithoutOrigin_IsNotChecked() {
    using var client = _factory.CreateClient();

    var response = await client.GetAsync("/tags", _cancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Preflight_FromTheAllowedOrigin_AllowsCredentials() {
    using var request = new HttpRequestMessage(HttpMethod.Options, "/tags");
    request.Headers.Add(HeaderNames.Origin, _allowedOrigin);
    request.Headers.Add(HeaderNames.AccessControlRequestMethod, "POST");
    request.Headers.Add(HeaderNames.AccessControlRequestHeaders, HeaderNames.ContentType);

    var response = await _client.SendAsync(request, _cancellationToken);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal(_allowedOrigin, Assert.Single(response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin)));
    Assert.Equal("true", Assert.Single(response.Headers.GetValues(HeaderNames.AccessControlAllowCredentials)));
    Assert.Contains(HeaderNames.Origin, response.Headers.Vary);
    Assert.True(response.Headers.Contains(HeaderNames.AccessControlMaxAge));
  }

  [Fact]
  public async Task Preflight_FromAnotherOrigin_GetsNoCorsHeaders() {
    using var client = _factory.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Options, "/tags");
    request.Headers.Add(HeaderNames.Origin, "https://evil.example");
    request.Headers.Add(HeaderNames.AccessControlRequestMethod, "POST");

    var response = await client.SendAsync(request, _cancellationToken);

    Assert.False(response.Headers.Contains(HeaderNames.AccessControlAllowOrigin));
  }
}
