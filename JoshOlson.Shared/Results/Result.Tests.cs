using Xunit;

namespace JoshOlson.Shared.Results;

public sealed class ResultTests {
  private static Result<Foo, FooErrors> Parse(string text)
    => text.Length > 0 ? new Foo(text) : new FooErrors("The text is empty.");

  [Fact]
  public void Result_FromAValue_HoldsItAndGivesItBack() {
    var result = Parse("bar");

    Assert.Equal(new Foo("bar"), Assert.IsType<Foo>(result.Value));
    Assert.Equal(new Foo("bar"), result.OrThrow());
    Assert.Equal("bar", result switch {
      Foo foo => foo.Text,
      FooErrors errors => errors.Message,
    });
  }

  [Fact]
  public void Result_FromErrors_HoldsThemAndThrowsTheirException() {
    var result = Parse("");

    Assert.Equal("The text is empty.", Assert.IsType<FooErrors>(result.Value).Message);
    Assert.True(result is FooErrors { Message.Length: > 0 });
    var exception = Assert.Throws<FooException>(() => _ = result.OrThrow());
    Assert.Equal("The text is empty.", exception.Message);
    // The exception is made by the throw, so the trace starts at `OrThrow` and not where the errors were found.
    Assert.Contains(nameof(Result<,>.OrThrow), exception.StackTrace!.Split('\n')[0], StringComparison.Ordinal);
    Assert.DoesNotContain(nameof(Parse), exception.StackTrace, StringComparison.Ordinal);
  }

  private sealed record Foo(string Text);

  private sealed record FooErrors(string Message) : IErrors<FooErrors> {
    public static Exception ToException(FooErrors errors)
      => new FooException(errors.Message);
  }

#pragma warning disable RCS1194 // A test's exception: only the message constructor is used
  private sealed class FooException : Exception {
    public FooException(string message)
      : base(message) { }
  }
#pragma warning restore RCS1194
}
