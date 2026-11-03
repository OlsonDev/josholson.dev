using Xunit;

namespace JoshOlson.Shared.Time;

public class DurationTextTests {
  private const long _Second = 1_000_000;
  private const long _Minute = 60 * _Second;
  private const long _Hour = 60 * _Minute;
  private const long _Day = 24 * _Hour;

  [Theory]
  [InlineData(0, "0s")]
  [InlineData(1, "0.000001s")]
  [InlineData(-1, "-0.000001s")]
  [InlineData(10, "0.00001s")]
  [InlineData(12_300, "0.0123s")]
  [InlineData(999_999, "0.999999s")]
  [InlineData(_Second, "1s")]
  [InlineData(_Second + 1, "1.000001s")]
  [InlineData(1_500_000, "1.5s")]
  [InlineData(59 * _Second + 999_999, "59.999999s")]
  [InlineData(_Minute, "1m")]
  [InlineData(_Minute + 1, "1m 0.000001s")]
  [InlineData(_Minute + _Second, "1m 1s")]
  [InlineData(_Hour, "1h")]
  [InlineData(_Hour - 1, "59m 59.999999s")]
  [InlineData(_Day, "1d")]
  [InlineData(_Day - 1, "23h 59m 59.999999s")]
  [InlineData(_Day + 1, "1d 0.000001s")]
  [InlineData(-3 * _Day - 4 * _Hour - 12 * _Minute - 12_300, "-3d 4h 12m 0.0123s")]
  [InlineData(3 * _Day + 12 * _Hour, "3d 12h")]
  [InlineData(long.MaxValue, "106751991d 4h 54.775807s")]
  [InlineData(long.MinValue, "-106751991d 4h 54.775808s")]
  public void Format_WritesTheCanonicalForm(long microseconds, string expected)
    => Assert.Equal(expected, DurationText.Format(microseconds));

  [Theory]
  [InlineData("0s", 0)]
  [InlineData("-0s", 0)]
  [InlineData("+5s", 5 * _Second)]
  [InlineData("0.000001s", 1)]
  [InlineData("0.5s", 500_000)]
  [InlineData("0.50s", 500_000)]
  [InlineData("90m", 90 * _Minute)]
  [InlineData("36h", 36 * _Hour)]
  [InlineData("1d 12h", _Day + 12 * _Hour)]
  [InlineData("1d 0h 0m 0s", _Day)]
  [InlineData("04h 05m 06s", 4 * _Hour + 5 * _Minute + 6 * _Second)]
  [InlineData("-3d 4h 12m 0.0123s", -3 * _Day - 4 * _Hour - 12 * _Minute - 12_300)]
  public void Parse_AcceptsTheCanonicalFormAndLenientVariants(string text, long expected)
    => Assert.Equal(expected, DurationText.Parse(text));

  [Theory]
  [InlineData("")]
  [InlineData(" ")]
  [InlineData("-")]
  [InlineData("+")]
  [InlineData("3")]
  [InlineData("d")]
  [InlineData("1")]
  [InlineData("1S")]
  [InlineData("1D")]
  [InlineData("1 s")]
  [InlineData("3d4h")]
  [InlineData("4h 3d")]
  [InlineData("3d 3d")]
  [InlineData("1h 1h")]
  [InlineData("1.5m")]
  [InlineData("1.5d")]
  [InlineData("0.0000001s")]
  [InlineData("1.s")]
  [InlineData(".5s")]
  [InlineData("1s ")]
  [InlineData(" 1s")]
  [InlineData("1d  1h")]
  [InlineData("--1s")]
  [InlineData("-3d -4h")]
  [InlineData("1e3s")]
  [InlineData("1,5s")]
  [InlineData("1ms")]
  [InlineData("106751992d")]
  [InlineData("9223372036854775808s")]
  [InlineData("18446744073709551616s")]
  public void TryParse_Malformed_ReturnsFalse(string text)
    => Assert.False(DurationText.TryParse(text, out _));

  [Fact]
  public void Parse_Malformed_ThrowsAndNamesTheText() {
    var exception = Assert.Throws<FormatException>(() => DurationText.Parse("3 days"));

    Assert.Contains("'3 days'", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void RoundTrip_EveryMicrosecondAroundZero() {
    for (var microseconds = -1_100_000L; microseconds <= 1_100_000; microseconds++) {
      Assert.Equal(microseconds, DurationText.Parse(DurationText.Format(microseconds)));
    }
  }

  [Fact]
  public void RoundTrip_OneMicrosecondEitherSideOfEveryUnitBoundary() {
    foreach (var unit in new[] { _Second, _Minute, _Hour, _Day, 7 * _Day, 365 * _Day }) {
      foreach (var multiple in new[] { 1L, 2, 59, 60, 61, 1_000 }) {
        var boundary = unit * multiple;
        foreach (var microseconds in new[] { boundary - 1, boundary, boundary + 1 }) {
          Assert.Equal(microseconds, DurationText.Parse(DurationText.Format(microseconds)));
          Assert.Equal(-microseconds, DurationText.Parse(DurationText.Format(-microseconds)));
        }
      }
    }
  }

  [Fact]
  public void RoundTrip_TheExtremes() {
    foreach (var microseconds in new[] { long.MinValue, long.MinValue + 1, long.MaxValue - 1, long.MaxValue }) {
      Assert.Equal(microseconds, DurationText.Parse(DurationText.Format(microseconds)));
    }
  }

  [Fact]
  public void RoundTrip_RandomMicrosecondsAcrossTheWholeRange() {
    var random = new Random(20261103);
    for (var i = 0; i < 100_000; i++) {
      var microseconds = random.NextInt64(long.MinValue, long.MaxValue);
      Assert.Equal(microseconds, DurationText.Parse(DurationText.Format(microseconds)));
    }
  }

  [Fact]
  public void Format_OfAnyParsedText_IsStable() {
    foreach (var text in new[] { "90m", "36h", "04h 05m 06s", "0.50s", "+5s", "-0s" }) {
      var canonical = DurationText.Format(DurationText.Parse(text));

      Assert.Equal(canonical, DurationText.Format(DurationText.Parse(canonical)));
    }
  }
}
