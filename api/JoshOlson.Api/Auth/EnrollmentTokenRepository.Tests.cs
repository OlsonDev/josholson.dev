using JoshOlson.Api.Common.Testing;
using NodaTime;
using Xunit;

namespace JoshOlson.Api.Auth;

public class EnrollmentTokenRepositoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly FakeClock _clock;
  private readonly AuthTestData _authTestData;
  private readonly EnrollmentTokenRepository _enrollmentTokenRepository;

  public EnrollmentTokenRepositoryTests(
    FakeClock clock,
    AuthTestData authTestData,
    EnrollmentTokenRepository enrollmentTokenRepository
  ) {
    _clock = clock;
    _authTestData = authTestData;
    _enrollmentTokenRepository = enrollmentTokenRepository;
  }

  [Fact]
  public async Task Use_WorksOnceAndOnlyBeforeExpiration() {
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      var now = _clock.GetCurrentInstant();
      var tokenHash = OpaqueToken.Hash(OpaqueToken.Create());
      await _enrollmentTokenRepository.CreateAsync(userId, tokenHash, now + Duration.FromMinutes(5), _cancellationToken);

      Assert.Equal(userId, await _enrollmentTokenRepository.FindUserIdAsync(tokenHash, now, _cancellationToken));
      var expired = now + Duration.FromMinutes(6);
      Assert.Null(await _enrollmentTokenRepository.FindUserIdAsync(tokenHash, expired, _cancellationToken));
      Assert.False(await _enrollmentTokenRepository.UseAsync(tokenHash, expired, _cancellationToken));

      var used = now + Duration.FromMinutes(1);
      Assert.True(await _enrollmentTokenRepository.UseAsync(tokenHash, used, _cancellationToken));
      Assert.False(await _enrollmentTokenRepository.UseAsync(tokenHash, used, _cancellationToken));
      Assert.Null(await _enrollmentTokenRepository.FindUserIdAsync(tokenHash, now, _cancellationToken));
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task FindUserId_UnknownToken_ReturnsNull() {
    var userId = await _enrollmentTokenRepository.FindUserIdAsync(
      OpaqueToken.Hash(OpaqueToken.Create()),
      _clock.GetCurrentInstant(),
      _cancellationToken);

    Assert.Null(userId);
  }
}
