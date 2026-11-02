using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Xunit;

namespace JoshOlson.Api.Auth;

public class SessionHttpContextExtensionsTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly IServiceProvider _services;
  private readonly AuthOptions _options;
  private readonly AuthTestData _authTestData;
  private readonly SessionService _sessionService;

  public SessionHttpContextExtensionsTests(
    IServiceProvider services,
    IOptions<AuthOptions> options,
    AuthTestData authTestData,
    SessionService sessionService
  ) {
    _services = services;
    _options = options.Value;
    _authTestData = authTestData;
    _sessionService = sessionService;
  }

  [Fact]
  public void GetSession_BeforeTheSessionFilterRan_Throws() {
    var context = new DefaultHttpContext();

    Assert.Throws<InvalidOperationException>(context.GetSession);
  }

  [Fact]
  public async Task GetSession_AfterTheSessionFilterAuthenticated_ReturnsTheUser() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var token = await _sessionService.StartAsync(userId, null, null, _cancellationToken);
      var httpContext = new DefaultHttpContext { RequestServices = _services };
      httpContext.Request.Headers.Cookie = $"{_options.CookieName}={token}";
      var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
      var context = new AuthorizationFilterContext(actionContext, []);

      await new SessionFilter().OnAuthorizationAsync(context);

      Assert.Null(context.Result);
      Assert.Equal(userId, httpContext.GetSession().User.UserId);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }
}
