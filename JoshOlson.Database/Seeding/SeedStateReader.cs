using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using JoshOlson.Database.Layers;
using JoshOlson.Database.Schema;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Seeding;

// Reads the rows of a database into the same `SeedState` a chain of layers merges to, typed through the lock, so the two can
// be compared and a layer can be made from the difference (`export-seed`). Every table the lock has is read except those
// marked `@seed ignore` (runtime data, which no layer holds), and every column but those generated from an expression (a
// layer cannot give one a value, so it is never in a state a layer makes).
//
// Each cell comes back as the text Postgres prints for it, with the session pinned to UTC and ISO dates inside one
// repeatable-read transaction, so the rows are one consistent picture, and is then read as its column's type, one place for
// all of them (an array comes back as an array of those texts):
// - a relative `timestamptz` is the signed whole microseconds from the database's anchor (`seed.hydration`), never from the
//   clock, and an absolute one is the UTC instant;
// - `bytea` is lower-case hex, `jsonb` the canonical JSON text (`JsonCanonical`), and `inet` an address with a prefix only
//   when it is not a whole host, as `SeedValueReader` reads the same values from a file.
// A database that was not built by the seed tool has no anchor, and is refused.
public static class SeedStateReader {
  public static async Task<DatabaseState> ReadAsync(NpgsqlConnection connection, SeedSchema schema, CancellationToken cancellationToken) {
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
    await ExecuteAsync(connection, transaction, "SET LOCAL TIME ZONE 'UTC'; SET LOCAL DateStyle = 'ISO, YMD'; SET LOCAL bytea_output = 'hex';", cancellationToken);
    var anchor = await ReadAnchorAsync(connection, transaction, cancellationToken);
    var state = new SeedState();
    foreach (var table in schema.InsertOrder().Where(table => !table.Table.Ignored)) {
      state.Tables[table.Table.Name] = await ReadTableAsync(connection, transaction, table, anchor, cancellationToken);
    }
    await transaction.CommitAsync(cancellationToken);
    return new(state, anchor);
  }

  private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken) {
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    _ = await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<Instant> ReadAnchorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken) {
    await using var command = new NpgsqlCommand("SELECT anchor_utc FROM seed.hydration;", connection, transaction);
    var anchor = await command.ExecuteScalarAsync(cancellationToken);
    return anchor is DateTime utc
      ? Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
      : throw new SeedFormatException("The database has no seed.hydration row, so it was not built by the seed tool and has no anchor to measure relative times from.");
  }

  private static async Task<SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>>> ReadTableAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    SeedTable table,
    Instant anchor,
    CancellationToken cancellationToken
  ) {
    var name = table.Table.Name;
    if (table.Table.PrimaryKey.Count is 0) {
      throw new SeedFormatException($"{name} has no primary key, and a layer finds its rows by it.");
    }
    var columns = table.Table.Columns
      .Where(column => column.GeneratedAs is not (GeneratedAs.ExpressionStored or GeneratedAs.ExpressionVirtual))
      .Select(column => table.Columns[column.Name])
      .ToList();
    var select = string.Join(", ", columns.Select(column => Quote(column.Column.Name) + (column.Type.IsArray ? "::text[]" : "::text")));
    var rows = new SortedDictionary<SeedValue[], Dictionary<string, SeedValue?>>(RowKeyComparer.Instance);
    await using var command = new NpgsqlCommand($"SELECT {select} FROM {string.Join('.', name.Split('.').Select(Quote))};", connection, transaction);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken)) {
      var values = new Dictionary<string, SeedValue?>(columns.Count, StringComparer.Ordinal);
      for (var i = 0; i < columns.Count; i++) {
        values[columns[i].Column.Name] = await ReadCellAsync(reader, i, name, columns[i], anchor, cancellationToken);
      }
      rows[[.. table.Table.PrimaryKey.Select(key => values[key]!)]] = values;
    }
    return rows;
  }

  private static async Task<SeedValue?> ReadCellAsync(
    NpgsqlDataReader reader,
    int ordinal,
    string table,
    SeedColumn column,
    Instant anchor,
    CancellationToken cancellationToken
  ) {
    if (await reader.IsDBNullAsync(ordinal, cancellationToken)) {
      return null;
    }
    if (!column.Type.IsArray) {
      return Convert(table, column, reader.GetString(ordinal), anchor);
    }
    var items = await reader.GetFieldValueAsync<string?[]>(ordinal, cancellationToken);
    return new SeedValue.Array([.. items.Select(item => item is null ? null : Convert(table, column, item, anchor))]);
  }

  private static SeedValue Convert(string table, SeedColumn column, string text, Instant anchor) {
    try {
      return column.Type.Seed switch {
        SeedType.Text => new SeedValue.Text(text),
        SeedType.Boolean => new SeedValue.Boolean(text is "true"),
        SeedType.SmallInt or SeedType.Integer or SeedType.BigInt => new SeedValue.Integer(long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
        SeedType.Decimal => new SeedValue.Decimal(decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)),
        SeedType.TimestampTz => Timestamp(column, text, anchor),
        SeedType.Bytea => new SeedValue.Bytes(text.StartsWith("\\x", StringComparison.Ordinal) ? text[2..].ToLowerInvariant() : throw new FormatException("not hex")),
        SeedType.Jsonb => new SeedValue.Json(JsonCanonical.Normalize(text)),
        SeedType.Inet => new SeedValue.Inet(Inet(text)),
        _ => throw new InvalidOperationException($"No reader for the seed type {column.Type.Seed}."),
      };
    } catch (Exception exception) when (exception is FormatException or OverflowException or System.Text.Json.JsonException) {
      throw new SeedFormatException(
        $"{table}.{column.Column.Name} holds '{text}', which is not a {column.Type.Seed} value ({exception.Message}).",
        exception);
    }
  }

  private static SeedValue Timestamp(SeedColumn column, string text, Instant anchor) {
    var instant = Instant.FromDateTimeOffset(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    return column.Column.Policy is TimePolicy.Absolute
      ? new SeedValue.AbsoluteTime(instant)
      : new SeedValue.RelativeTime((instant.ToUnixTimeTicks() - anchor.ToUnixTimeTicks()) / 10);
  }

  // `inet` as Postgres prints it is an address and a prefix length, which for a whole host is left off in a layer.
  private static string Inet(string text) {
    var parts = text.Split('/');
    var address = IPAddress.Parse(parts[0]);
    var maximum = address.AddressFamily is AddressFamily.InterNetwork ? 32 : 128;
    return parts.Length is 1 || int.Parse(parts[1], CultureInfo.InvariantCulture) == maximum
      ? address.ToString()
      : string.Create(CultureInfo.InvariantCulture, $"{address}/{parts[1]}");
  }

  private static string Quote(string name)
    => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
