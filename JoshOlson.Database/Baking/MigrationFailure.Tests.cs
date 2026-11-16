using Xunit;

namespace JoshOlson.Database.Baking;

public sealed class MigrationFailureTests {
  [Fact]
  public void FirstError_OutputWithAnErrorLine_IsThatLineTrimmed() {
    var failure = new MigrationFailure(
      "podman exec foo exited with 1:\nApplying R__bar.sql\n  ERROR: Migration failed for R__bar.sql: relation \"baz\" does not exist  \nERROR: qux\n");

    Assert.Equal("ERROR: Migration failed for R__bar.sql: relation \"baz\" does not exist", failure.FirstError);
  }

  [Fact]
  public void FirstError_OutputWithNoErrorLine_IsTheWholeOutputTrimmed() {
    var failure = new MigrationFailure("  podman exec foo exited with 1:\nquux\n");

    Assert.Equal("podman exec foo exited with 1:\nquux", failure.FirstError);
  }
}
