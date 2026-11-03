using System.Net;
using JoshOlson.Api.Common.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class SessionFilterTests {
  // Every public endpoint, by name. Adding one here is the explicit decision that it needs no session.
  private static readonly string[] _PublicActions = [
    "EnrollmentController.Enroll",
    "EnrollmentController.Options",
    "SignInController.Options",
    "SignInController.SignIn",
    "SignInController.SignOut",
    "TagController.Get",
    "TagController.GetAll",
  ];

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;
  private readonly NpgsqlConnection _connection;
  private readonly AuthTestData _authTestData;

  public SessionFilterTests(
    WebApplicationFactory<Program> factory,
    HttpClient client,
    NpgsqlConnection connection,
    AuthTestData authTestData
  ) {
    _factory = factory;
    _client = client;
    _connection = connection;
    _authTestData = authTestData;
  }

  [Fact]
  public void EveryEndpoint_RequiresASessionUnlessListedAsPublic() {
    var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

    var publicActions = endpoints
      .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
      .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>())
      .OfType<ControllerActionDescriptor>()
      .Select(action => $"{action.ControllerTypeInfo.Name}.{action.MethodInfo.Name}")
      .Distinct(StringComparer.Ordinal)
      .Order(StringComparer.Ordinal);

    Assert.Equal(_PublicActions, publicActions);
  }

  [Theory]
  [InlineData("POST", "/tags")]
  [InlineData("PUT", "/tags/1")]
  [InlineData("DELETE", "/tags/1")]
  [InlineData("GET", "/auth/me")]
  [InlineData("GET", "/auth/passkeys")]
  [InlineData("POST", "/auth/passkeys/registration/options")]
  public async Task ProtectedEndpoint_WithoutASession_ReturnsUnauthorized(string method, string url) {
    using var request = new HttpRequestMessage(new(method), url);

    var response = await _client.SendAsync(request, _cancellationToken);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task PublicEndpoint_WithoutASession_IsAllowed() {
    var response = await _client.GetAsync("/tags", _cancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task ProtectedEndpoint_WithAnExpiredSession_ReturnsUnauthorized() {
    var userId = await _authTestData.SignInAsync(_client, _cancellationToken);
    try {
      // Signed in: the action runs, and the tag does not exist.
      Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/tags/0", _cancellationToken)).StatusCode);

      var sql = """
        UPDATE auth.session AS s SET
            created_utc    = now() - interval '2 seconds'
          , last_seen_utc  = now() - interval '2 seconds'
          , expires_utc    = now() - interval '1 second'
        WHERE s.user_id = @userId;
        """;
      await _connection.ExecuteAsync(sql, new { userId }, _cancellationToken);

      Assert.Equal(HttpStatusCode.Unauthorized, (await _client.DeleteAsync("/tags/0", _cancellationToken)).StatusCode);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }
}
