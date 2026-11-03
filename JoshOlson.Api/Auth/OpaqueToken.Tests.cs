using Xunit;

namespace JoshOlson.Api.Auth;

public class OpaqueTokenTests {
  [Fact]
  public void Create_IsRandomAndUrlSafe() {
    var tokens = Enumerable.Range(0, 20).Select(_ => OpaqueToken.Create()).ToList();

    Assert.Equal(20, tokens.Distinct(StringComparer.Ordinal).Count());
    Assert.All(tokens, token => {
      Assert.True(token.Length >= 43, "at least 256 bits");
      Assert.DoesNotContain(token, character => character is '+' or '/' or '=');
    });
  }

  [Fact]
  public void Hash_IsSha256OfTheToken() {
    var hash = OpaqueToken.Hash("foo");

    Assert.Equal(32, hash.Length);
    Assert.Equal(hash, OpaqueToken.Hash("foo"));
    Assert.NotEqual(hash, OpaqueToken.Hash("bar"));
  }

  [Fact]
  public void NewUserHandle_FitsTheColumnAndIsRandom() {
    var handle = OpaqueToken.NewUserHandle();

    Assert.InRange(handle.Length, 16, 64);
    Assert.NotEqual(handle, OpaqueToken.NewUserHandle());
  }
}
