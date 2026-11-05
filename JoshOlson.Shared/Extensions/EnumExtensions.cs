namespace JoshOlson.Shared.Extensions;

public static class EnumExtensions {
  extension<T>(T value) where T : struct, Enum {
    // The member's name in kebab-case: `SetNull` is `set-null`. Throws for a value that is not a member, since its text
    // would be a number.
    public string ToKebabCase()
      => Enum.IsDefined(value)
        ? value.ToString().ToKebabCase()
        : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a member of the enum.");
  }

  extension(Enum) {
    // The member whose kebab-case name is exactly `text`. Strict on purpose: no other casing, no missing hyphens, and no
    // numbers or comma lists, which Enum.TryParse would accept. The text must be what `ToKebabCase` writes.
    public static bool TryParseKebabCase<T>(string text, out T value) where T : struct, Enum {
      value = default;
      if (!Enum.TryParse(text.Replace("-", "", StringComparison.Ordinal), ignoreCase: true, out T parsed)
        || !Enum.IsDefined(parsed)
        || !string.Equals(parsed.ToKebabCase(), text, StringComparison.Ordinal)) {
        return false;
      }
      value = parsed;
      return true;
    }
  }
}
