using Xunit;

namespace JoshOlson.Api.Auth;

public class RemovalOutcomeTests {
  // The controller maps each value to a status code, so a renumbered or reused value would change what a client sees.
  [Theory]
  [InlineData(RemovalOutcome.Started, 1)]
  [InlineData(RemovalOutcome.NotFound, 2)]
  [InlineData(RemovalOutcome.LastPasskey, 3)]
  public void Values_AreExplicitAndNeverReused(RemovalOutcome outcome, int number)
    => Assert.Equal(number, (int)outcome);
}
