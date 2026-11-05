using JoshOlson.Api.Common.Testing;
using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class SessionServiceTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly NpgsqlConnection _connection;
  private readonly FakeClock _clock;
  private readonly AuthTestData _authTestData;
  private readonly SessionRepository _sessionRepository;
  private readonly SessionService _sessionService;

  public SessionServiceTests(
    NpgsqlConnection connection,
    FakeClock clock,
    AuthTestData authTestData,
    SessionRepository sessionRepository,
    SessionService sessionService
  ) {
    _connection = connection;
    _clock = clock;
    _authTestData = authTestData;
    _sessionRepository = sessionRepository;
    _sessionService = sessionService;
  }

  [Fact]
  public async Task Authenticate_SlidesTheIdleExpirationButNotPastTheAbsoluteLimit() {
    var start = _clock.GetCurrentInstant();
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var token = await _sessionService.StartAsync(userId, null, null, _cancellationToken);
      Assert.Equal(userId, (await _sessionService.AuthenticateAsync(token, _cancellationToken))?.User.UserId);

      // Six days in: still valid, and the idle expiration slides to a week from now.
      _clock.Advance(Duration.FromDays(6));
      Assert.NotNull(await _sessionService.AuthenticateAsync(token, _cancellationToken));
      var hash = OpaqueToken.Hash(token);
      var slid = await _sessionRepository.GetActiveAsync(hash, _clock.GetCurrentInstant(), _cancellationToken);
      Assert.Equal(start + Duration.FromDays(13), slid?.ExpiresUtc);

      // Keep using it every six days; the absolute limit (30 days) still ends it.
      for (var day = 12; day <= 24; day += 6) {
        _clock.Advance(Duration.FromDays(6));
        Assert.NotNull(await _sessionService.AuthenticateAsync(token, _cancellationToken));
      }
      _clock.Advance(Duration.FromDays(6) + Duration.FromSeconds(1));
      Assert.Null(await _sessionService.AuthenticateAsync(token, _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task Authenticate_AfterTheIdleLifetime_ReturnsNull() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var token = await _sessionService.StartAsync(userId, null, null, _cancellationToken);

      _clock.Advance(Duration.FromDays(8));

      Assert.Null(await _sessionService.AuthenticateAsync(token, _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task End_RemovesTheSession() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var token = await _sessionService.StartAsync(userId, null, null, _cancellationToken);

      Assert.True(await _sessionService.EndAsync(token, _cancellationToken));
      Assert.Null(await _sessionService.AuthenticateAsync(token, _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task Authenticate_UnknownToken_ReturnsNull()
    => Assert.Null(await _sessionService.AuthenticateAsync(OpaqueToken.Create(), _cancellationToken));

  [Fact]
  public async Task Start_StoresTheTrimmedUserAgentOnceAndNothingForABlankOne() {
    var userAgent = $"test-{Guid.NewGuid():N}";
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      await _sessionService.StartAsync(userId, null, $"  {userAgent}\n", _cancellationToken);
      await _sessionService.StartAsync(userId, null, userAgent, _cancellationToken);
      await _sessionService.StartAsync(userId, null, "  ", _cancellationToken);

      var sql = """
        SELECT
            ua.value  AS user_agent
        FROM (SELECT)         AS _
        JOIN auth.session     AS s   ON s.user_id         = @userId
        JOIN http.user_agent  AS ua  ON ua.user_agent_id  = s.user_agent_id;
        """;
      var stored = await _connection.QueryAsync<string>(sql, new { userId }, _cancellationToken);
      Assert.Equal([userAgent, userAgent], stored);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
      await DeleteUserAgentAsync(userAgent);
    }
  }

  // Cleanup runs in a `finally`, so it ignores the test's cancellation.
  private async Task DeleteUserAgentAsync(string userAgent) {
    var sql = """
      DELETE FROM http.user_agent AS ua
      WHERE ua.value = @userAgent;
      """;
    await _connection.ExecuteAsync(sql, new { userAgent }, CancellationToken.None);
  }
}
