using System.Globalization;

namespace JoshOlson.Shared.Extensions;

public static class NumberExtensions {
  extension(int value) {
    // For messages and file text: the same digits whatever the machine's culture (MA0076 flags the implicit form).
    public string ToInvariantString()
      => value.ToString(CultureInfo.InvariantCulture);
  }

  extension(long value) {
    public string ToInvariantString()
      => value.ToString(CultureInfo.InvariantCulture);
  }
}
