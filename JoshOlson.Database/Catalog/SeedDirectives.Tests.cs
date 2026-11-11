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
  [InlineData(null, false)]
  [InlineData("", false)]
  [InlineData("Who is logged in.", false)]
  [InlineData("@seed ignore", true)]
  [InlineData("Who is logged in. @seed ignore", true)]
  [InlineData("@seed ignore\nwho is logged in", true)]
  [InlineData("  @seed   ignore  ", true)]
  [InlineData("@seed ignore and again @seed ignore", true)]
  [InlineData("email@seed.example ignore", false)]
  public void TableIgnoreOf_FindsTheDirective(string? comment, bool expected) {
    var (ignored, problem) = SeedDirectives.TableIgnoreOf(comment, "foo.bar");

    Assert.Equal(expected, ignored);
    Assert.Null(problem);
  }

  [Theory]
  [InlineData("@seed")]
  [InlineData("@seed sometimes")]
  [InlineData("@seed Ignore")]
  [InlineData("@seed absolute")]
  [InlineData("@seed ignore @seed relative")]
  public void TableIgnoreOf_BadDirective_ReportsAProblemNamingTheTable(string comment) {
    var (ignored, problem) = SeedDirectives.TableIgnoreOf(comment, "foo.bar");

    Assert.False(ignored);
    Assert.StartsWith("foo.bar:", problem, StringComparison.Ordinal);
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

  [Fact]
  public void TimePolicyOf_IgnoreOnAColumn_IsAProblemSinceTheDirectiveIsForTables() {
    var (policy, problem) = SeedDirectives.TimePolicyOf("@seed ignore", "foo.bar.baz");

    Assert.Null(policy);
    Assert.NotNull(problem);
  }
}
