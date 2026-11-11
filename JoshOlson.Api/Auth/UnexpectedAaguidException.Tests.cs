using Xunit;

namespace JoshOlson.Api.Auth;

public class UnexpectedAaguidExceptionTests {
  [Fact]
  public void Message_NamesTheModelWhenItIsKnown() {
    var exception = new UnexpectedAaguidException(AaguidLookupTests.Aaguid, "Foo Key");

    Assert.Contains(AaguidLookupTests.Aaguid.ToString(), exception.Message, StringComparison.Ordinal);
    Assert.Contains("(Foo Key)", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Message_SaysSoWhenTheModelIsUnknown() {
    var exception = new UnexpectedAaguidException(AaguidLookupTests.Aaguid, modelName: null);

    Assert.Contains("(model unknown)", exception.Message, StringComparison.Ordinal);
    Assert.Contains("`aaguid` column", exception.Message, StringComparison.Ordinal);
  }
}
