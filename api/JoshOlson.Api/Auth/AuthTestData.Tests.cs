using System.Security.Cryptography;
using JoshOlson.Api.Common.Extensions;
using Npgsql;

namespace JoshOlson.Api.Auth;

// Users the auth tests create and remove. Deleting a user also deletes its passkeys and sessions.
public static class AuthTestData {
  public static byte[] NewHandle()
    => RandomNumberGenerator.GetBytes(32);

  public static async Task<int> CreateUserAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    => await new AuthUserRepository(connection).CreateAsync($"test-{Guid.NewGuid():N}", NewHandle(), cancellationToken);

  // Cleanup runs in a `finally`, so it ignores the test's cancellation.
  public static async Task DeleteUserAsync(NpgsqlConnection connection, int userId) {
    var sql = """
      DELETE FROM auth.user AS u
      WHERE u.user_id = @userId;
      """;
    var parameters = new { userId };
    await connection.ExecuteAsync(sql, parameters, CancellationToken.None);
  }
}
