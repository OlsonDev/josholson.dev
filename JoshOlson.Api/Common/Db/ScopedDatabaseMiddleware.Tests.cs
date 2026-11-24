using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace JoshOlson.Api.Common.Db;

public sealed class ScopedDatabaseMiddlewareTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly ScopedDatabaseTestData _scopedDatabaseTestData;
  private readonly WebApplicationFactory<Program> _factory;

  public ScopedDatabaseMiddlewareTests(
    ScopedDatabaseTestData scopedDatabaseTestData,
    WebApplicationFactory<Program> factory
  ) {
    _scopedDatabaseTestData = scopedDatabaseTestData;
    _factory = factory;
  }

  [Fact]
  public async Task Get_UnknownScope_ReturnsBadRequest() {
    await using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting(ScopedDatabaseRegistration.SettingName, "true"));
    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(ScopedDatabaseMiddleware.HeaderName, "nosuchscope");

    var response = await client.GetAsync("/tags", _cancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Get_NoScopeHeader_RunsOnTheDefaultDatabase() {
    await using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting(ScopedDatabaseRegistration.SettingName, "true"));
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/tags", _cancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_ScopeHeaderWithTheSettingOff_IsIgnored() {
    using var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Add(ScopedDatabaseMiddleware.HeaderName, "nosuchscope");

    var response = await client.GetAsync("/tags", _cancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Get_ScopeWithADatabase_RunsOnIt() {
    var id = await _scopedDatabaseTestData.CreateAsync(_cancellationToken);
    await using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting(ScopedDatabaseRegistration.SettingName, "true"));
    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(ScopedDatabaseMiddleware.HeaderName, id);

    // The database has no tables, so the same request that works on the default database fails on it: the request
    // reached it, and not the default one.
    var response = await client.GetAsync("/tags", _cancellationToken);

    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
  }
}
