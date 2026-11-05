namespace JoshOlson.Database.Locks;

// A column's base type (a domain is recorded as the type it is built on) and whether it is a one-dimensional array.
// The text form is how the lock writes it: `bigint`, `timestamptz`, `smallint[]`.
public readonly record struct ColumnType(SeedType Element, bool IsArray) {
  public static bool TryParse(string text, out ColumnType type) {
    var isArray = text.EndsWith("[]", StringComparison.Ordinal);
    SeedType? element = (isArray ? text[..^2] : text) switch {
      "text" => SeedType.Text,
      "boolean" => SeedType.Boolean,
      "smallint" => SeedType.SmallInt,
      "integer" => SeedType.Integer,
      "bigint" => SeedType.BigInt,
      "timestamptz" => SeedType.TimestampTz,
      "bytea" => SeedType.Bytea,
      "jsonb" => SeedType.Jsonb,
      "inet" => SeedType.Inet,
      _ => null,
    };
    type = element is { } found ? new(found, isArray) : default;
    return element is not null;
  }

  public static ColumnType Parse(string text)
    => TryParse(text, out var type) ? type : throw new SeedFormatException($"'{text}' is not a column type the seed tool supports.");

  public override string ToString()
    => ElementName() + (IsArray ? "[]" : "");

  private string ElementName()
    => Element switch {
      SeedType.Text => "text",
      SeedType.Boolean => "boolean",
      SeedType.SmallInt => "smallint",
      SeedType.Integer => "integer",
      SeedType.BigInt => "bigint",
      SeedType.TimestampTz => "timestamptz",
      SeedType.Bytea => "bytea",
      SeedType.Jsonb => "jsonb",
      SeedType.Inet => "inet",
      _ => throw new InvalidOperationException($"Unknown seed type {Element}."),
    };
}
