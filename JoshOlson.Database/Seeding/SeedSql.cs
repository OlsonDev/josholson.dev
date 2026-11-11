using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Extensions;
using NodaTime;
using Npgsql;
using NpgsqlTypes;

namespace JoshOlson.Database.Seeding;

// Turns the rows a chain of layers comes to into `INSERT` statements, one table at a time in an order the foreign keys
// allow (`SchemaLock.InsertOrder`). A table's rows share one statement, so its column list is every column any of them
// names, in the table's column order, and a row that leaves one out says `DEFAULT`. A column the database fills itself
// (an identity column) is inserted into with `OVERRIDING SYSTEM VALUE`, so a seed's explicit IDs stand and the identity
// sequence is left where it was (ADR 0015, seed ID blocks).
//
// Values are typed parameters. A relative time is the anchor plus its offset, worked out here, exact to the microsecond.
// A table with more rows than fit in the parameter limit is split into several statements, which keep the key order.
public static class SeedSql {
  // Postgres takes 65,535 parameters in a statement; this stays well under it.
  private const int _MaxParameters = 60_000;

  public static List<SeedStatement> Build(SeedState state, SeedSchema schema, Instant anchor) {
    var statements = new List<SeedStatement>();
    var problems = new List<string>();
    foreach (var table in schema.InsertOrder()) {
      if (state.Tables.TryGetValue(table.Table.Name, out var rows) && rows.Count > 0) {
        AddTable(table, rows, anchor, statements, problems);
      }
    }
    if (problems.Count > 0) {
      throw new SeedFormatException(problems.Count is 1
        ? problems[0]
        : $"The rows have {problems.Count.ToInvariantString()} problems:\n- {string.Join("\n- ", problems)}");
    }
    return statements;
  }

  private static void AddTable(
    SeedTable table,
    SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>> rows,
    Instant anchor,
    List<SeedStatement> statements,
    List<string> problems
  ) {
    var columns = new List<SeedColumn>();
    foreach (var column in table.Table.Columns) {
      if (rows.Values.Any(row => row.ContainsKey(column.Name))) {
        columns.Add(table.Columns[column.Name]);
      }
    }
    var perStatement = Math.Max(1, _MaxParameters / columns.Count);
    foreach (var chunk in rows.Values.Chunk(perStatement)) {
      statements.Add(Statement(table, columns, chunk, anchor, problems));
    }
  }

  private static SeedStatement Statement(
    SeedTable table,
    List<SeedColumn> columns,
    Dictionary<string, SeedValue?>[] rows,
    Instant anchor,
    List<string> problems
  ) {
    var sql = new StringBuilder();
    sql.Append("INSERT INTO ").Append(table.Table.Name).Append(" (\n");
    for (var i = 0; i < columns.Count; i++) {
      sql.Append(i is 0 ? "    " : "  , ").Append(columns[i].Column.Name).Append('\n');
    }
    sql.Append(')');
    if (columns.Any(column => column.Column.GeneratedAs is GeneratedAs.IdentityAlways or GeneratedAs.IdentityByDefault)) {
      sql.Append(" OVERRIDING SYSTEM VALUE");
    }
    sql.Append(" VALUES\n");

    var parameters = new List<NpgsqlParameter>();
    for (var r = 0; r < rows.Length; r++) {
      sql.Append(r is 0 ? "    (" : "  , (");
      for (var c = 0; c < columns.Count; c++) {
        if (c > 0) {
          sql.Append(", ");
        }
        sql.Append(Placeholder(table, columns[c], rows[r], anchor, parameters, problems));
      }
      sql.Append(")\n");
    }
    sql.Append(';');
    return new() {
      Table = table.Table.Name,
      RowCount = rows.Length,
      Sql = sql.ToString(),
      Parameters = parameters,
    };
  }

  // `DEFAULT` when the row leaves the column out, `NULL` when it says NULL, and otherwise the next parameter.
  private static string Placeholder(
    SeedTable table,
    SeedColumn column,
    Dictionary<string, SeedValue?> row,
    Instant anchor,
    List<NpgsqlParameter> parameters,
    List<string> problems
  ) {
    if (!row.TryGetValue(column.Column.Name, out var value)) {
      return "DEFAULT";
    }
    if (value is null) {
      return "NULL";
    }
    try {
      var name = string.Create(CultureInfo.InvariantCulture, $"p{parameters.Count}");
      parameters.Add(new(name, TypeOf(column.Type)) { Value = Convert(column.Type.Seed, value, anchor) });
      return "@" + name;
    } catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException or InvalidOperationException) {
      problems.Add($"{table.Table.Name}: {column.Column.Name} holds a time outside the dates the anchor and a timestamptz can reach.");
      return "NULL";
    }
  }

  private static NpgsqlDbType TypeOf(ResolvedType type) {
    var element = type.Seed switch {
      SeedType.Text => NpgsqlDbType.Text,
      SeedType.Boolean => NpgsqlDbType.Boolean,
      SeedType.SmallInt => NpgsqlDbType.Smallint,
      SeedType.Integer => NpgsqlDbType.Integer,
      SeedType.BigInt => NpgsqlDbType.Bigint,
      SeedType.Decimal => NpgsqlDbType.Numeric,
      SeedType.TimestampTz => NpgsqlDbType.TimestampTz,
      SeedType.Bytea => NpgsqlDbType.Bytea,
      SeedType.Jsonb => NpgsqlDbType.Jsonb,
      SeedType.Inet => NpgsqlDbType.Inet,
      _ => throw new InvalidOperationException($"No parameter type for the seed type {type.Seed}."),
    };
#pragma warning disable RCS1130 // NpgsqlDbType is a flags enum that Npgsql does not mark [Flags]: Array is OR-ed with the element type.
    return type.IsArray ? NpgsqlDbType.Array | element : element;
#pragma warning restore RCS1130
  }

  private static object Convert(SeedType seed, SeedValue value, Instant anchor)
    => value is SeedValue.Array array
      ? ConvertArray(seed, array.Items, anchor)
      : ConvertScalar(seed, value, anchor);

  private static object ConvertScalar(SeedType seed, SeedValue value, Instant anchor)
    => (seed, value) switch {
      (SeedType.Text, SeedValue.Text text) => text.Value,
      (SeedType.Boolean, SeedValue.Boolean boolean) => boolean.Value,
      (SeedType.SmallInt, SeedValue.Integer integer) => (short)integer.Value,
      (SeedType.Integer, SeedValue.Integer integer) => (int)integer.Value,
      (SeedType.BigInt, SeedValue.Integer integer) => integer.Value,
      (SeedType.Decimal, SeedValue.Decimal number) => number.Value,
      (SeedType.TimestampTz, SeedValue.RelativeTime offset) => anchor.Plus(Duration.FromNanoseconds(new BigInteger(offset.Microseconds) * 1000)).ToDateTimeUtc(),
      (SeedType.TimestampTz, SeedValue.AbsoluteTime instant) => instant.Value.ToDateTimeUtc(),
      (SeedType.Bytea, SeedValue.Bytes bytes) => System.Convert.FromHexString(bytes.Hex),
      (SeedType.Jsonb, SeedValue.Json json) => json.Value,
      (SeedType.Inet, SeedValue.Inet inet) => ParseInet(inet.Value),
      _ => throw new InvalidOperationException($"A {value.GetType().Name} is not a {seed} value."),
    };

  // An array parameter needs a typed array, with a nullable element where an item may be NULL.
  private static Array ConvertArray(SeedType seed, List<SeedValue?> items, Instant anchor)
    => seed switch {
      SeedType.Text => Reference(items, item => (string)ConvertScalar(seed, item, anchor)),
      SeedType.Jsonb => Reference(items, item => (string)ConvertScalar(seed, item, anchor)),
      SeedType.Bytea => Reference(items, item => (byte[])ConvertScalar(seed, item, anchor)),
      SeedType.Boolean => Value(items, item => (bool)ConvertScalar(seed, item, anchor)),
      SeedType.SmallInt => Value(items, item => (short)ConvertScalar(seed, item, anchor)),
      SeedType.Integer => Value(items, item => (int)ConvertScalar(seed, item, anchor)),
      SeedType.BigInt => Value(items, item => (long)ConvertScalar(seed, item, anchor)),
      SeedType.Decimal => Value(items, item => (decimal)ConvertScalar(seed, item, anchor)),
      SeedType.TimestampTz => Value(items, item => (DateTime)ConvertScalar(seed, item, anchor)),
      SeedType.Inet => Value(items, item => ((IPAddress, int))ConvertScalar(seed, item, anchor)),
      _ => throw new InvalidOperationException($"No array conversion for the seed type {seed}."),
    };

  private static T?[] Value<T>(List<SeedValue?> items, Func<SeedValue, T> convert)
    where T : struct {
    var result = new T?[items.Count];
    for (var i = 0; i < result.Length; i++) {
      result[i] = items[i] is { } item ? convert(item) : null;
    }
    return result;
  }

  private static T?[] Reference<T>(List<SeedValue?> items, Func<SeedValue, T> convert)
    where T : class {
    var result = new T?[items.Count];
    for (var i = 0; i < result.Length; i++) {
      result[i] = items[i] is { } item ? convert(item) : null;
    }
    return result;
  }

  // The reader already made sure the address parses; a network size, when it was written, is the part after the slash.
  private static (IPAddress Address, int Bits) ParseInet(string text) {
    var parts = text.Split('/');
    var address = IPAddress.Parse(parts[0]);
    var bits = parts.Length > 1
      ? int.Parse(parts[1], CultureInfo.InvariantCulture)
      : address.GetAddressBytes().Length * 8;
    return (address, bits);
  }
}
