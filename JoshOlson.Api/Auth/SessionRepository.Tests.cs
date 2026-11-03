using System.Net;
using JoshOlson.Api.Common.Testing;
using NodaTime;
using Xunit;

namespace JoshOlson.Api.Auth;

public class SessionRepositoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly FakeClock _clock;
  private readonly AuthTestData _authTestData;
  private readonly SessionRepository _sessionRepository;

  public SessionRepositoryTests(FakeClock clock, AuthTestData authTestData, SessionRepository sessionRepository) {
    _clock = clock;
    _authTestData = authTestData;
    _sessionRepository = sessionRepository;
  }

  [Fact]
  public async Task Lifecycle_CreatesFindsExtendsAndDeletes() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var now = _clock.GetCurrentInstant();
      var tokenHash = OpaqueToken.Hash(OpaqueToken.Create());
      var sessionId = await _sessionRepository.CreateAsync(
        new() {
          UserId = userId,
          TokenHash = tokenHash,
          ExpiresUtc = now + Duration.FromHours(1),
          Ip = IPAddress.Parse("192.0.2.7"),
          UserAgentId = null,
        },
        _cancellationToken);

      var found = await _sessionRepository.GetActiveAsync(tokenHash, now, _cancellationToken);
      Assert.Equal(sessionId, found?.SessionId);
      Assert.Equal(userId, found?.UserId);

      var later = now + Duration.FromDays(1);
      Assert.True(await _sessionRepository.ExtendExpirationAsync(sessionId, later, later, _cancellationToken));
      Assert.Equal(later, (await _sessionRepository.GetActiveAsync(tokenHash, now, _cancellationToken))?.ExpiresUtc);

      Assert.True(await _sessionRepository.DeleteAsync(tokenHash, _cancellationToken));
      Assert.Null(await _sessionRepository.GetActiveAsync(tokenHash, now, _cancellationToken));
      Assert.False(await _sessionRepository.DeleteAsync(tokenHash, _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task GetActive_ExpiredSession_ReturnsNull() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var now = _clock.GetCurrentInstant();
      var tokenHash = OpaqueToken.Hash(OpaqueToken.Create());
      await _sessionRepository.CreateAsync(
        new() {
          UserId = userId,
          TokenHash = tokenHash,
          ExpiresUtc = now + Duration.FromHours(1),
          Ip = null,
          UserAgentId = null,
        },
        _cancellationToken);

      Assert.Null(await _sessionRepository.GetActiveAsync(tokenHash, now + Duration.FromHours(2), _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }
}
