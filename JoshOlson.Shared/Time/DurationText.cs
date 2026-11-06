using System.Globalization;

namespace JoshOlson.Shared.Time;

// A duration as text, in whole microseconds (Postgres's resolution): `-3d 4h 12m 0.0123s`. Used for the offsets a seed
// stores relative to its anchor. Everything is exact integer arithmetic, so formatting and parsing round-trip.
//
// Canonical form (what Format writes):
// - Units are `d`, `h`, `m`, `s`, lowercase, largest first, one space between them, none repeated.
// - A unit that is zero is left out, and there are no leading zeros, so `1d 12h` and never `36h` or `1d 0h 12h`.
// - Seconds carry a fraction of up to six digits with trailing zeros dropped (`0.0123s`, `1.5s`).
// - A duration of zero is `0s`. A negative one starts with `-`, which applies to the whole duration.
//
// TryParse accepts the same shape, plus a leading `+`, leading zeros, and any size for the largest unit (`90m`).
// It rejects a fraction on any unit but seconds, more than six fractional digits (nothing is rounded), a repeated
// or out-of-order unit, extra spaces, and values that do not fit in a `long` of microseconds.
public static class DurationText {
  private const ulong _MicrosPerSecond = 1_000_000;
  private const ulong _MicrosPerMinute = 60 * _MicrosPerSecond;
  private const ulong _MicrosPerHour = 60 * _MicrosPerMinute;
  private const ulong _MicrosPerDay = 24 * _MicrosPerHour;
  private const int _FractionDigits = 6;

  public static string Format(long microseconds) {
    if (microseconds is 0) {
      return "0s";
    }
    var negative = microseconds < 0;
    // Negating long.MinValue overflows, so work on the magnitude as an unsigned value.
    var magnitude = negative ? (ulong)-(microseconds + 1) + 1 : (ulong)microseconds;

    var parts = new List<string>();
    AddUnit(parts, magnitude / _MicrosPerDay, 'd');
    AddUnit(parts, magnitude % _MicrosPerDay / _MicrosPerHour, 'h');
    AddUnit(parts, magnitude % _MicrosPerHour / _MicrosPerMinute, 'm');
    var seconds = magnitude % _MicrosPerMinute / _MicrosPerSecond;
    var fraction = magnitude % _MicrosPerSecond;
    if (seconds > 0 || fraction > 0) {
      var digits = fraction is 0
        ? ""
        : "." + fraction.ToString($"D{_FractionDigits}", CultureInfo.InvariantCulture).TrimEnd('0');
      parts.Add(FormattableString.Invariant($"{seconds}{digits}s"));
    }
    return (negative ? "-" : "") + string.Join(' ', parts);
  }

  public static bool TryParse(string text, out long microseconds) {
    microseconds = 0;
    var negative = text.StartsWith('-');
    var body = negative || text.StartsWith('+') ? text[1..] : text;
    if (body.Length is 0) {
      return false;
    }

    UInt128 total = 0;
    var previousRank = int.MaxValue;
    foreach (var token in body.Split(' ')) {
      var rank = token.Length > 1 ? RankOf(token[^1]) : -1;
      if (rank < 0 || rank >= previousRank || !TryParseAmount(token[..^1], rank is 0, out var amount, out var fraction)) {
        return false;
      }
      previousRank = rank;
      total += (UInt128)amount * UnitMicroseconds(rank) + fraction;
    }

    // A negative duration may be one microsecond longer than a positive one: long.MinValue has no positive twin.
    var limit = (UInt128)long.MaxValue + (negative ? 1UL : 0UL);
    if (total > limit) {
      return false;
    }
    microseconds = negative ? total == limit ? long.MinValue : -(long)total : (long)total;
    return true;
  }

  public static long Parse(string text)
    => TryParse(text, out var microseconds)
      ? microseconds
      : throw new FormatException($"'{text}' is not a duration such as '-3d 4h 12m 0.0123s'.");

  private static void AddUnit(List<string> parts, ulong amount, char unit) {
    if (amount > 0) {
      parts.Add(FormattableString.Invariant($"{amount}{unit}"));
    }
  }

  private static int RankOf(char unit)
    => unit switch {
      'd' => 3,
      'h' => 2,
      'm' => 1,
      's' => 0,
      _ => -1,
    };

  private static ulong UnitMicroseconds(int rank)
    => rank switch {
      3 => _MicrosPerDay,
      2 => _MicrosPerHour,
      1 => _MicrosPerMinute,
      _ => _MicrosPerSecond,
    };

  // The number before a unit: whole digits, and for seconds an optional `.` and one to six fractional digits.
  private static bool TryParseAmount(string number, bool isSeconds, out ulong amount, out ulong fractionMicroseconds) {
    fractionMicroseconds = 0;
    var dot = isSeconds ? number.IndexOf('.', StringComparison.Ordinal) : -1;
    var whole = dot < 0 ? number : number[..dot];
    if (!ulong.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out amount)) {
      return false;
    }
    if (dot < 0) {
      return true;
    }
    var digits = number[(dot + 1)..];
    return digits.Length is > 0 and <= _FractionDigits
      && digits.All(char.IsAsciiDigit)
      && ulong.TryParse(digits.PadRight(_FractionDigits, '0'), NumberStyles.None, CultureInfo.InvariantCulture, out fractionMicroseconds);
  }
}
