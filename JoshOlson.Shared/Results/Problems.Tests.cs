using Xunit;

namespace JoshOlson.Shared.Results;

public sealed class ProblemsTests {
  [Fact]
  public void ToString_OneProblem_IsItsSentence() {
    var problems = new FooProblems("The foos have");
    problems.Add("bar is not baz.");

    Assert.Equal("bar is not baz.", problems.ToString());
    Assert.True(problems.Any);
  }

  [Fact]
  public void ToString_SeveralWithASubject_IsAHeadingAndAList() {
    var problems = new FooProblems("The foos have", "qux problems");
    problems.Add("bar");
    problems.AddRange(["baz", "quux"]);

    Assert.Equal("The foos have 3 qux problems:\n- bar\n- baz\n- quux", problems.ToString());
    Assert.Equal(3, problems.Count);
  }

  [Fact]
  public void ToString_SeveralWithNoSubject_IsALineEach() {
    var problems = new FooProblems(subject: null);
    problems.AddRange(["bar", "baz"]);

    Assert.Equal("bar\nbaz", problems.ToString());
  }

  [Fact]
  public void ToString_None_IsEmpty() {
    var problems = new FooProblems("The foos have");

    Assert.Equal("", problems.ToString());
    Assert.False(problems.Any);
  }

  private sealed class FooProblems : Problems {
    public FooProblems(string? subject, string noun = "problems")
      : base(subject, noun) { }
  }
}
