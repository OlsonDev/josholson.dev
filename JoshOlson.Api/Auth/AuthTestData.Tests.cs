using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Fido2NetLib;
using JoshOlson.Api.Common.Testing;
using JoshOlson.Shared.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Auth;

// Users the auth tests create and remove. Deleting a user also deletes its passkeys and sessions. A test takes this in
// its constructor: `Startup` registers every `*TestData` class as scoped.
public sealed class AuthTestData {
  // The API writes NodaTime types as ISO strings; a test client reading a response needs the same settings.
  private static readonly JsonSerializerOptions _JsonOptions
    = new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

  private readonly NpgsqlConnection _connection;
  private readonly FakeClock _clock;
  private readonly AuthOptions _options;
  private readonly WebApplicationFactory<Program> _factory;
  private readonly AuthUserRepository _authUserRepository;
  private readonly SessionRepository _sessionRepository;
  private readonly EnrollmentTokenRepository _enrollmentTokenRepository;

  public AuthTestData(
    NpgsqlConnection connection,
    FakeClock clock,
    IOptions<AuthOptions> options,
    WebApplicationFactory<Program> factory,
    AuthUserRepository authUserRepository,
    SessionRepository sessionRepository,
    EnrollmentTokenRepository enrollmentTokenRepository
  ) {
    _connection = connection;
    _clock = clock;
    _options = options.Value;
    _factory = factory;
    _authUserRepository = authUserRepository;
    _sessionRepository = sessionRepository;
    _enrollmentTokenRepository = enrollmentTokenRepository;
  }

  public static byte[] NewHandle()
    => RandomNumberGenerator.GetBytes(32);

  // A stand-in security key that acts from the allowed origin, unless a test says otherwise.
  public SoftwareAuthenticator NewAuthenticator(string? origin = null)
    => new(origin ?? _options.AllowedOrigin, _options.RelyingPartyId);

  // Another browser: no cookies, and the allowed `Origin` every request that changes state needs.
  public HttpClient NewBrowser() {
    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Add(HeaderNames.Origin, _options.AllowedOrigin);
    return client;
  }

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

  // A new user with one passkey, registered through the enrollment endpoints the way the CLI does, which also signs
  // `client` in. Returns the user's id, for `DeleteUserAsync`.
  public async Task<int> EnrollUserAsync(
    HttpClient client,
    SoftwareAuthenticator authenticator,
    CancellationToken cancellationToken
  ) {
    var userId = await CreateUserAsync(cancellationToken);
    try {
      var token = await CreateEnrollmentTokenAsync(userId, cancellationToken);
      var response = await EnrollAsync(client, token, authenticator, "first", cancellationToken);
      Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
      return userId;
    } catch {
      // The caller never gets an id to clean up, so a failure here would leak the user.
      await DeleteUserAsync(userId);
      throw;
    }
  }

  // Mints an enrollment token for the user, the way the CLI does, and returns it.
  public async Task<string> CreateEnrollmentTokenAsync(int userId, CancellationToken cancellationToken) {
    var token = OpaqueToken.Create();
    var expiresUtc = _clock.GetCurrentInstant() + Duration.FromMinutes(5);
    await _enrollmentTokenRepository.CreateAsync(userId, OpaqueToken.Hash(token), expiresUtc, cancellationToken);
    return token;
  }

  // Registers the authenticator through the enrollment endpoints, which also signs the client in.
  public static async Task<HttpResponseMessage> EnrollAsync(
    HttpClient client,
    string token,
    SoftwareAuthenticator authenticator,
    string nickname,
    CancellationToken cancellationToken
  ) {
    var optionsResponse = await client.PostAsJsonAsync("/auth/enrollment/options", new { token }, cancellationToken);
    Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
    var started = await optionsResponse.Content
      .ReadFromJsonAsync<CeremonyResponse<CredentialCreateOptions>>(cancellationToken);
    var request = new EnrollmentRequest {
      Token = token,
      AttemptToken = started!.AttemptToken,
      Nickname = nickname,
      Response = authenticator.Create(started.Options),
    };
    return await client.PostAsJsonAsync("/auth/enrollment", request, cancellationToken);
  }

  public static async Task<List<PasskeyResponse>> ListPasskeysAsync(
    HttpClient client,
    CancellationToken cancellationToken
  ) {
    var passkeys = await client.GetFromJsonAsync<List<PasskeyResponse>>(
      "/auth/passkeys",
      _JsonOptions,
      cancellationToken);
    return passkeys!;
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
