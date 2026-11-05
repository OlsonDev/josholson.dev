using System.Collections.Concurrent;

namespace JoshOlson.Database.Schema;

// A column's type in the cleanest spelling a `CREATE TABLE` would use: a supported base type named for its `SeedType`
// (`timestamptz`, `decimal(9, 6)`, `smallint[]`), or the schema-qualified name of a domain (`foo.bar`, `foo.bar[]`) that
// the lock defines. The catalog reader is what puts Postgres's own spellings (`timestamp with time zone`, `numeric(9,6)`)
// into this form, so anything else written in the lock is refused. `Modifiers` is the text inside the parentheses, kept
// as it is.
public readonly record struct ColumnType(string Name, string? Modifiers, bool IsArray) {
  public bool IsDomain
    => Name.Contains('.');

  // The base type, or null for a domain (whose base type the lock says).
  public SeedType? Seed
    => IsDomain ? null : SeedOf(Name);

  // The same few spellings come up for every column and again for every step of a domain chain, so each is parsed once.
  // The result depends on the text alone, so the cache serves a lock being built and one being read alike.
  private static readonly ConcurrentDictionary<string, ColumnType?> _Parsed = new(StringComparer.Ordinal);

  public static bool TryParse(string text, out ColumnType type) {
    var parsed = _Parsed.GetOrAdd(text, ParseUncached);
    type = parsed.GetValueOrDefault();
    return parsed.HasValue;
  }

  private static ColumnType? ParseUncached(string text) {
    var isArray = text.EndsWith("[]", StringComparison.Ordinal);
    var rest = isArray ? text[..^2] : text;
    string? modifiers = null;
    var open = rest.IndexOf('(');
    if (open >= 0) {
      if (!rest.EndsWith(')')) {
        return null;
      }
      modifiers = rest[(open + 1)..^1];
      rest = rest[..open];
    }

    if (rest.Contains('.')) {
      if (modifiers is not null || !IsDomainName(rest)) {
        return null;
      }
    } else if (SeedOf(rest) is not { } seed || !AcceptsModifiers(seed, modifiers)) {
      return null;
    }
    return new(rest, modifiers, isArray);
  }

  public static ColumnType Parse(string text)
    => TryParse(text, out var type) ? type : throw new SeedFormatException($"'{text}' is not a column type the seed tool supports.");

  // `schema.name`, with no part empty and none of the characters that make up type syntax.
  public static bool IsDomainName(string name)
    => name.Split('.') is [{ Length: > 0 } schema, { Length: > 0 } domain]
      && schema.All(IsNameCharacter)
      && domain.All(IsNameCharacter);

  public override string ToString()
    => Name + (Modifiers is null ? "" : $"({Modifiers})") + (IsArray ? "[]" : "");

  // The `SeedType` member is the type's name in lower case, so no table of names is needed. The letters-only test keeps
  // out the numbers and comma lists that Enum.TryParse would also accept.
  private static SeedType? SeedOf(string name)
    => name.Length > 0 && name.All(char.IsAsciiLetterLower) && Enum.TryParse<SeedType>(name, ignoreCase: true, out var seed)
      ? seed
      : null;

  // Only `decimal` takes modifiers, a precision and optionally a scale: `timestamptz(3)` would silently round the exact
  // microsecond offsets, so it is not a seed type.
  private static bool AcceptsModifiers(SeedType seed, string? modifiers)
    => modifiers is null
      || seed is SeedType.Decimal && modifiers.Split(", ") is { Length: <= 2 } numbers && numbers.All(IsWholeNumber);

  private static bool IsWholeNumber(string text)
    => text.Length > 0 && text.All(char.IsAsciiDigit) && (text.Length is 1 || text[0] is not '0');

  private static bool IsNameCharacter(char character)
    => character is not ('(' or ')' or '[' or ']' or ',' or ' ');
}
