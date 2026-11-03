using Flurl.Http.Testing;
using Xunit;

namespace JoshOlson.Api.Auth;

public class AaguidLookupTests {
  public static readonly Guid Aaguid = Guid.Parse("eabb46cc-e241-80bf-ae9e-96fa6d2975cf");

  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly AaguidLookup _aaguidLookup;

  public AaguidLookupTests(AaguidLookup aaguidLookup)
    => _aaguidLookup = aaguidLookup;

  [Fact]
  public async Task FindName_KnownAaguid_ReturnsTheModelName() {
    using var httpTest = new HttpTest();
    httpTest.RespondWithJson(new Dictionary<string, object> { [Aaguid.ToString()] = new { name = "Foo Key" } });

    Assert.Equal("Foo Key", await _aaguidLookup.FindNameAsync(Aaguid, _cancellationToken));
  }

  [Fact]
  public async Task FindName_UnknownAaguid_ReturnsNull() {
    using var httpTest = new HttpTest();
    httpTest.RespondWithJson(new Dictionary<string, object>());

    Assert.Null(await _aaguidLookup.FindNameAsync(Aaguid, _cancellationToken));
  }

  [Theory]
  [InlineData(500, "oops")]
  [InlineData(200, "not json")]
  public async Task FindName_WhenTheLookupFails_ReturnsNull(int status, string body) {
    using var httpTest = new HttpTest();
    httpTest.RespondWith(body, status);

    Assert.Null(await _aaguidLookup.FindNameAsync(Aaguid, _cancellationToken));
  }

  [Fact]
  public async Task FindName_WhenTheRequestTimesOut_ReturnsNull() {
    using var httpTest = new HttpTest();
    httpTest.SimulateTimeout();

    Assert.Null(await _aaguidLookup.FindNameAsync(Aaguid, _cancellationToken));
  }
}
