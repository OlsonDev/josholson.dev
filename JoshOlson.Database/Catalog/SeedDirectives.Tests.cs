using JoshOlson.Database.Schema;
using Xunit;

namespace JoshOlson.Database.Catalog;

public class SeedDirectivesTests {
  [Theory]
  [InlineData(null, null)]
  [InlineData("", null)]
  [InlineData("When it happened.", null)]
  [InlineData("@seed absolute", TimePolicy.Absolute)]
  [InlineData("@seed relative", TimePolicy.Relative)]
  [InlineData("When it happened. @seed absolute", TimePolicy.Absolute)]
  [InlineData("@seed absolute\nwhen it happened", TimePolicy.Absolute)]
  [InlineData("  @seed   absolute  ", TimePolicy.Absolute)]
  [InlineData("@seed absolute and again @seed absolute", TimePolicy.Absolute)]
  [InlineData("email@seed.example absolute", null)]
  public void TimePolicyOf_FindsTheDirective(string? comment, TimePolicy? expected) {
    var (policy, problem) = SeedDirectives.TimePolicyOf(comment, "foo.bar");

    Assert.Equal(expected, policy);
    Assert.Null(problem);
  }

  [Theory]
  [InlineData("@seed")]
  [InlineData("@seed sometimes")]
  [InlineData("@seed Absolute")]
  [InlineData("@seed absolute @seed relative")]
  public void TimePolicyOf_BadDirective_ReportsAProblemNamingTheColumn(string comment) {
    var (policy, problem) = SeedDirectives.TimePolicyOf(comment, "foo.bar.baz");

    Assert.Null(policy);
    Assert.StartsWith("foo.bar.baz:", problem, StringComparison.Ordinal);
  }
}
