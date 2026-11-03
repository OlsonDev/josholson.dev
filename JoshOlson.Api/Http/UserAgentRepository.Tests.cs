using JoshOlson.Api.Common.Extensions;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Http;

public class UserAgentRepositoryTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly NpgsqlConnection _connection;
  private readonly UserAgentRepository _userAgentRepository;

  public UserAgentRepositoryTests(NpgsqlConnection connection, UserAgentRepository userAgentRepository) {
    _connection = connection;
    _userAgentRepository = userAgentRepository;
  }

  [Fact]
  public async Task GetOrCreateId_StoresEachValueOnce() {
    var foo = $"test-{Guid.NewGuid():N}";
    var bar = $"test-{Guid.NewGuid():N}";
    var fooId = await _userAgentRepository.GetOrCreateIdAsync(foo, _cancellationToken);
    var barId = 0;
    try {
      barId = await _userAgentRepository.GetOrCreateIdAsync(bar, _cancellationToken);

      Assert.Equal(fooId, await _userAgentRepository.GetOrCreateIdAsync(foo, _cancellationToken));
      Assert.NotEqual(fooId, barId);
    } finally {
      await DeleteAsync(fooId);
      await DeleteAsync(barId);
    }
  }

  [Fact]
  public async Task GetOrCreateId_ValueOverTheMaximumLength_ViolatesCheck() {
    var tooLong = new string('x', UserAgentRepository.MaxLength + 1);

    var exception = await Assert.ThrowsAsync<PostgresException>(
      () => _userAgentRepository.GetOrCreateIdAsync(tooLong, _cancellationToken));

    Assert.Equal("ck_user_agent_value_length", exception.ConstraintName);
  }

  // Cleanup runs in a `finally`, so it ignores the test's cancellation.
  private async Task DeleteAsync(int userAgentId) {
    var sql = """
      DELETE FROM http.user_agent AS ua
      WHERE ua.user_agent_id = @userAgentId;
      """;
    await _connection.ExecuteAsync(sql, new { userAgentId }, CancellationToken.None);
  }
}
