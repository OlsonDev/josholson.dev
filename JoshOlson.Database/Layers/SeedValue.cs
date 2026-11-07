using NodaTime;

namespace JoshOlson.Database.Layers;

// One value in a layer, typed by its column (ADR 0035, typed seed values). SQL NULL is a C# `null`, never a case here.
// Each case holds the value in a canonical form, so two layers that mean the same value compare equal:
// - `Text`: the text as written.
// - `Boolean`, `Integer` (`smallint`, `integer`, and `bigint`, range-checked against the column), and `Decimal`.
// - `RelativeTime`: whole microseconds from the database's anchor, for a `timestamptz` column that is relative.
// - `AbsoluteTime`: the instant, for one that is `@seed absolute`.
// - `Bytes`: lower-case hex digits, with no `\x` prefix.
// - `Json`: compact JSON text.
// - `Inet`: the address, and `/bits` when the value had a prefix length.
// - `Array`: the elements, each of which may be NULL.
public abstract record SeedValue {
  public sealed record Text(string Value) : SeedValue;

  public sealed record Boolean(bool Value) : SeedValue;

  public sealed record Integer(long Value) : SeedValue;

  public sealed record Decimal(decimal Value) : SeedValue;

  public sealed record RelativeTime(long Microseconds) : SeedValue;

  public sealed record AbsoluteTime(Instant Value) : SeedValue;

  public sealed record Bytes(string Hex) : SeedValue;

  public sealed record Json(string Value) : SeedValue;

  public sealed record Inet(string Value) : SeedValue;

  public sealed record Array(List<SeedValue?> Items) : SeedValue;
}
