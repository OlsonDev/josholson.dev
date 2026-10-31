using System.Security.Cryptography;
using JoshOlson.Api.Common.Testing;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

public class AuthUserRepositoryTests {
  [Fact]
  public async Task Create_ThenGetByIdAndHandle_ReturnsTheUser() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var authUserRepository = new AuthUserRepository(connection);
    var handle = AuthTestData.NewHandle();

    var userId = await authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", handle, cancellationToken);
    try {
      var byId = await authUserRepository.GetAsync(userId, cancellationToken);
      var byHandle = await authUserRepository.GetByHandleAsync(handle, cancellationToken);

      Assert.NotNull(byId);
      Assert.True(byId.IsActive);
      Assert.Equal(handle, byId.UserHandle);
      Assert.Equal(userId, byHandle?.UserId);
    } finally {
      await AuthTestData.DeleteUserAsync(connection, userId);
    }
  }

  [Fact]
  public async Task Get_UnknownUserOrHandle_ReturnsNull() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var authUserRepository = new AuthUserRepository(connection);

    Assert.Null(await authUserRepository.GetAsync(0, cancellationToken));
    Assert.Null(await authUserRepository.GetByHandleAsync(AuthTestData.NewHandle(), cancellationToken));
  }

  [Fact]
  public async Task Create_DuplicateHandle_ViolatesUniqueConstraint() {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var authUserRepository = new AuthUserRepository(connection);
    var handle = AuthTestData.NewHandle();
    var userId = await authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", handle, cancellationToken);
    try {
      var exception = await Assert.ThrowsAsync<PostgresException>(
        () => authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", handle, cancellationToken));
      Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    } finally {
      await AuthTestData.DeleteUserAsync(connection, userId);
    }
  }

  [Theory]
  [InlineData(15)]
  [InlineData(65)]
  public async Task Create_HandleOutsideTheAllowedLength_ViolatesCheck(int length) {
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var connection = await TestDatabase.DataSource.OpenConnectionAsync(cancellationToken);
    var authUserRepository = new AuthUserRepository(connection);

    var displayName = $"test-{Guid.NewGuid():N}";
    var handle = RandomNumberGenerator.GetBytes(length);

    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => authUserRepository.CreateAsync(displayName, handle, cancellationToken));
    Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
  }
}
