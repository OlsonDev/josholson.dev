namespace JoshOlson.Database.Schema;

// The column types a seed can hold. A type joins the list when a seeded column needs it, and the lock refuses a
// column of any other type, so an unsupported type is noticed when the lock is generated and not when a seed fails.
public enum SeedType {
  Text = 1,
  Boolean = 2,
  SmallInt = 3,
  Integer = 4,
  BigInt = 5,
  TimestampTz = 6,
  Bytea = 7,
  Jsonb = 8,
  Inet = 9,
  Decimal = 10,
}
