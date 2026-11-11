using Xunit;

namespace JoshOlson.Database.Locks;

// `@seed ignore` on a table (its rows are runtime data, not seed data) is `seed: ignore` in the lock.
public class SchemaLockSeedIgnoreTests {
  private static SchemaLock Lock(bool ignored) {
    var sample = SchemaLockSample.Create();
    var session = SchemaLockSample.NewTable("foo.session") with { Comment = "Sessions. @seed ignore", Ignored = ignored };
    return sample with { Tables = [.. sample.Tables, session] };
  }

  [Fact]
  public void Write_IgnoredTable_SaysSeedIgnoreRightAfterItsComment() {
    var text = SchemaLockWriter.Write(Lock(ignored: true));

    Assert.Contains("  foo.session:\n    comment: \"Sessions. @seed ignore\"\n    seed: ignore\n    columns:", text, StringComparison.Ordinal);
  }

  [Fact]
  public void Write_TableThatIsNotIgnored_SaysNothingAboutIt() {
    var text = SchemaLockWriter.Write(Lock(ignored: false));

    Assert.DoesNotContain("seed:", text, StringComparison.Ordinal);
  }

  [Fact]
  public void Parse_SeedIgnore_MarksOnlyThatTableAndWritesBackTheSameText() {
    var text = SchemaLockWriter.Write(Lock(ignored: true));

    var parsed = SchemaLockParser.Parse(text);

    Assert.Equal(["foo.session"], parsed.Tables.Where(table => table.Ignored).Select(table => table.Name));
    Assert.Equal(text, SchemaLockWriter.Write(parsed));
  }

  [Fact]
  public void Parse_NoSeedKey_IsNotIgnored()
    => Assert.DoesNotContain(SchemaLockParser.Parse(SchemaLockWriter.Write(Lock(ignored: false))).Tables, table => table.Ignored);

  [Theory]
  [InlineData("sometimes")]
  [InlineData("absolute")]
  [InlineData("true")]
  public void Parse_AnyOtherSeedValue_Throws(string value) {
    var text = SchemaLockWriter.Write(Lock(ignored: true)).Replace("    seed: ignore\n", $"    seed: {value}\n", StringComparison.Ordinal);

    var exception = Assert.Throws<SeedFormatException>(() => SchemaLockParser.Parse(text));

    Assert.Contains("table foo.session has seed", exception.Message, StringComparison.Ordinal);
    Assert.Contains("the only value is 'ignore'", exception.Message, StringComparison.Ordinal);
  }
}
