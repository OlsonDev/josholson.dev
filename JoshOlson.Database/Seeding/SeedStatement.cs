using Npgsql;

namespace JoshOlson.Database.Seeding;

// One `INSERT` for some rows of one table, with its parameters typed for Npgsql. Every value is a parameter, never SQL
// text, and a NULL is the literal `NULL`.
public sealed record SeedStatement {
  public required string Table { get; init; }
  public required int RowCount { get; init; }
  public required string Sql { get; init; }
  public required List<NpgsqlParameter> Parameters { get; init; }
}
