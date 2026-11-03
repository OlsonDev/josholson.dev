using NodaTime;
using Xunit;

namespace JoshOlson.Api.Auth;

[Collection(OwnerCollection.Name)]
public class EnrollCommandTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly IServiceProvider _services;
  private readonly AuthTestData _authTestData;
  private readonly EnrollmentTokenRepository _enrollmentTokenRepository;

  public EnrollCommandTests(
    IServiceProvider services,
    AuthTestData authTestData,
    EnrollmentTokenRepository enrollmentTokenRepository
  ) {
    _services = services;
    _authTestData = authTestData;
    _enrollmentTokenRepository = enrollmentTokenRepository;
  }

  [Fact]
  public async Task Run_PrintsATokenThatEnrollsTheOwner() {
    // The owner is the first user. Make sure there is one, so the command never creates an account here.
    var userId = await _authTestData.CreateUserAsync(_cancellationToken);
    try {
      await using var output = new StringWriter();

      var exitCode = await EnrollCommand.RunAsync(_services, [], output, _cancellationToken);

      Assert.Equal(0, exitCode);
      var token = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1].Trim();
      var tokenHash = OpaqueToken.Hash(token);
      var nowUtc = SystemClock.Instance.GetCurrentInstant();
      Assert.NotNull(await _enrollmentTokenRepository.FindUserIdAsync(tokenHash, nowUtc, _cancellationToken));
      await _enrollmentTokenRepository.UseAsync(tokenHash, nowUtc + Duration.FromSeconds(1), CancellationToken.None);
    } finally {
      await _authTestData.DeleteUserAsync(userId);
    }
  }
}
