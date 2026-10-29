using Xunit;

namespace JoshOlson.Api.Common.Validation;

public class ValidationErrorsTests {
  [Fact]
  public void ToProblemDetails_GroupsMessagesByFieldAndIs422() {
    var errors = new ValidationErrors();
    errors.Add("foo", "one");
    errors.Add("foo", "two");
    errors.Add("bar", "three");

    var details = errors.ToProblemDetails();

    Assert.True(errors.Any);
    Assert.True(errors.Has("foo"));
    Assert.False(errors.Has("baz"));
    Assert.Equal(422, details.Status);
    Assert.Equal(["one", "two"], details.Errors["foo"]);
    Assert.Equal(["three"], details.Errors["bar"]);
  }

  [Fact]
  public void New_HasNoErrors()
    => Assert.False(new ValidationErrors().Any);
}
