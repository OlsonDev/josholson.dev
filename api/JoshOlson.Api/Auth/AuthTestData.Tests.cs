using System.Security.Cryptography;
using JoshOlson.Api.Common.Extensions;
using Npgsql;

namespace JoshOlson.Api.Auth;

// Users the auth tests create and remove. Deleting a user also deletes its passkeys and sessions. A test takes this in
// its constructor: `Startup` registers every `*TestData` class as scoped.
public sealed class AuthTestData {
  private readonly NpgsqlConnection _connection;
  private readonly AuthUserRepository _authUserRepository;

  public AuthTestData(NpgsqlConnection connection, AuthUserRepository authUserRepository) {
    _connection = connection;
    _authUserRepository = authUserRepository;
  }

  public static byte[] NewHandle()
    => RandomNumberGenerator.GetBytes(32);

  public async Task<int> CreateUserAsync(CancellationToken cancellationToken)
    => await _authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", NewHandle(), cancellationToken);

  // Cleanup runs in a `finally`, so it ignores the test's cancellation.
  public async Task DeleteUserAsync(int userId) {
    var sql = """
      DELETE FROM auth.user AS u
      WHERE u.user_id = @userId;
      """;
    var parameters = new { userId };
    await _connection.ExecuteAsync(sql, parameters, CancellationToken.None);
  }
}
