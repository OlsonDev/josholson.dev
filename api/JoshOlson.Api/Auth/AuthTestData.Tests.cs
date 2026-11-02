using System.Security.Cryptography;
using JoshOlson.Api.Common.Extensions;
using JoshOlson.Api.Common.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using NodaTime;
using Npgsql;

namespace JoshOlson.Api.Auth;

// Users the auth tests create and remove. Deleting a user also deletes its passkeys and sessions. A test takes this in
// its constructor: `Startup` registers every `*TestData` class as scoped.
public sealed class AuthTestData {
  private readonly NpgsqlConnection _connection;
  private readonly FakeClock _clock;
  private readonly AuthOptions _options;
  private readonly AuthUserRepository _authUserRepository;
  private readonly SessionRepository _sessionRepository;

  public AuthTestData(
    NpgsqlConnection connection,
    FakeClock clock,
    IOptions<AuthOptions> options,
    AuthUserRepository authUserRepository,
    SessionRepository sessionRepository
  ) {
    _connection = connection;
    _clock = clock;
    _options = options.Value;
    _authUserRepository = authUserRepository;
    _sessionRepository = sessionRepository;
  }

  public static byte[] NewHandle()
    => RandomNumberGenerator.GetBytes(32);

  public async Task<int> CreateUserAsync(CancellationToken cancellationToken)
    => await _authUserRepository.CreateAsync($"test-{Guid.NewGuid():N}", NewHandle(), cancellationToken);

  // Signs `client` in as a new user: a session row, and the cookie the API would have set. Returns the user's id, for
  // `DeleteUserAsync`.
  public async Task<int> SignInAsync(HttpClient client, CancellationToken cancellationToken) {
    var userId = await CreateUserAsync(cancellationToken);
    var token = OpaqueToken.Create();
    var session = new NewSession {
      UserId = userId,
      TokenHash = OpaqueToken.Hash(token),
      ExpiresUtc = _clock.GetCurrentInstant() + Duration.FromHours(1),
      Ip = null,
      UserAgentId = null,
    };
    await _sessionRepository.CreateAsync(session, cancellationToken);
    client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{_options.CookieName}={token}");
    return userId;
  }

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
