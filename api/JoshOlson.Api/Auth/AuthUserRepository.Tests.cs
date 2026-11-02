using System.Security.Cryptography;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class AuthUserRepositoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly AuthTestData _authTestData;
  private readonly AuthUserRepository _authUserRepository;

  public AuthUserRepositoryTests(AuthTestData authTestData, AuthUserRepository authUserRepository) {
    _authTestData = authTestData;
    _authUserRepository = authUserRepository;
  }

  [Fact]
  public async Task Create_ThenGetByIdAndHandle_ReturnsTheUser() {
    var handle = AuthTestData.NewHandle();

    var userId = await _authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", handle, _cancellationToken);
    try {
      var byId = await _authUserRepository.GetAsync(userId, _cancellationToken);
      var byHandle = await _authUserRepository.GetByHandleAsync(handle, _cancellationToken);

      Assert.NotNull(byId);
      Assert.True(byId.IsActive);
      Assert.Equal(handle, byId.UserHandle);
      Assert.Equal(userId, byHandle?.UserId);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Fact]
  public async Task Get_UnknownUserOrHandle_ReturnsNull() {
    Assert.Null(await _authUserRepository.GetAsync(0, _cancellationToken));
    Assert.Null(await _authUserRepository.GetByHandleAsync(AuthTestData.NewHandle(), _cancellationToken));
  }

  [Fact]
  public async Task Create_DuplicateHandle_ViolatesUniqueConstraint() {
    var handle = AuthTestData.NewHandle();
    var userId = await _authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", handle, _cancellationToken);
    try {
      var exception = await Assert.ThrowsAsync<PostgresException>(
        () => _authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", handle, _cancellationToken));
      Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }

  [Theory]
  [InlineData(15)]
  [InlineData(65)]
  public async Task Create_HandleOutsideTheAllowedLength_ViolatesCheck(int length) {
    var displayName = $"test-{Guid.NewGuid():N}";
    var handle = RandomNumberGenerator.GetBytes(length);

    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => _authUserRepository.CreateAsync(displayName, handle, _cancellationToken));
    Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
  }
}
