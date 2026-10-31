using System.Globalization;
using Xunit;

namespace JoshOlson.Api.Common.Db;

public class SqlMemoizerTests {
  public enum Foo { Bar = 1, Baz = 2 }

  [Fact]
  public void ForValues_OneValue_BuildsOncePerValue() {
    var builds = 0;
    var memoizer = new SqlMemoizer<bool>(flag => { builds++; return $"SELECT {flag}"; });

    var first = memoizer.ForValues(true);
    var again = memoizer.ForValues(true);
    var other = memoizer.ForValues(false);

    Assert.Same(first, again);
    Assert.Equal("SELECT True", first);
    Assert.Equal("SELECT False", other);
    Assert.Equal(2, builds);
  }

  [Fact]
  public void ForValues_TwoValues_PassesThemInOrder() {
    var memoizer = new SqlMemoizer<bool, Foo>((flag, foo) => $"{flag}/{foo}");

    Assert.Equal("True/Baz", memoizer.ForValues(true, Foo.Baz));
    Assert.Equal("False/Bar", memoizer.ForValues(false, Foo.Bar));
  }

  [Fact]
  public void ForValues_ThreeValues_BuildsOncePerCombination() {
    var builds = 0;
    var memoizer = new SqlMemoizer<bool, bool, bool>((a, b, c) => { builds++; return $"{a}{b}{c}"; });

    var first = memoizer.ForValues(true, false, true);
    var again = memoizer.ForValues(true, false, true);
    var swapped = memoizer.ForValues(false, true, true);

    Assert.Same(first, again);
    Assert.NotEqual(first, swapped);
    Assert.Equal("TrueFalseTrue", first);
    Assert.Equal(2, builds);
  }

  [Fact]
  public void ForValues_FourValues_PassesThemInOrder() {
    var memoizer = new SqlMemoizer<bool, int, Foo, bool>(
      (a, b, c, d) => $"{a}/{b.ToString(CultureInfo.InvariantCulture)}/{c}/{d}");

    Assert.Equal("True/7/Baz/False", memoizer.ForValues(true, 7, Foo.Baz, false));
  }

  [Fact]
  public async Task ForValues_Concurrent_ReturnsOneValuePerCombination() {
    var cancellationToken = TestContext.Current.CancellationToken;
    var memoizer = new SqlMemoizer<bool>(flag => new string('x', flag ? 2 : 1));

    var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() => memoizer.ForValues(true), cancellationToken));
    var results = await Task.WhenAll(tasks);

    Assert.All(results, result => Assert.Equal("xx", result));
  }
}
