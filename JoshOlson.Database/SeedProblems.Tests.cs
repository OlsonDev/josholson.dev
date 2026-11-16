using Xunit;

namespace JoshOlson.Database;

public sealed class SeedProblemsTests {
  [Fact]
  public void ThrowIfAny_WithProblems_ThrowsThemAsOneSeedFormatException() {
    var problems = new SeedProblems("The foos have");
    problems.AddRange(["bar", "baz"]);

    var exception = Assert.Throws<SeedFormatException>(problems.ThrowIfAny);

    Assert.Equal("The foos have 2 problems:\n- bar\n- baz", exception.Message);
  }

  [Fact]
  public void ThrowIfAny_WithNone_DoesNothing() {
    new SeedProblems().ThrowIfAny();

    Assert.Equal("", new SeedProblems().ToString());
  }
}
