using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Baking;

// The one row of `seed.hydration` (`seed_schema.sql`), and the only SQL that touches the table: the layer a database was
// built from (none for one that holds the migrations alone), whether it was built for editing that layer, and the anchor
// its relative times are measured from.
//
// The anchor goes in and comes out as a `DateTime` in UTC, because these connections have no NodaTime plugin to write an
// `Instant` with.
public static class SeedHydration {
  // A database that was just filled. It has no row yet.
  public static async Task RecordAsync(
    NpgsqlConnection connection,
    string? layer,
    bool editing,
    Instant anchor,
    CancellationToken cancellationToken
  ) {
    var sql = """
      INSERT INTO seed.hydration (
          layer
        , editing
        , anchor_utc
      ) VALUES (
          @layer
        , @editing
        , @anchor
      );
      """;
    var parameters = new {
      layer,
      editing,
      anchor = anchor.ToDateTimeUtc(),
    };
    await connection.ExecuteAsync(sql, parameters, cancellationToken);
  }

  // A database a layer was just exported from, which now stands for that layer: the row it has keeps its anchor, and one
  // with no row gets the anchor the export measured from.
  public static async Task RecordExportAsync(
    NpgsqlConnection connection,
    string layer,
    Instant anchor,
    CancellationToken cancellationToken
  ) {
    var sql = """
      INSERT INTO seed.hydration (
          layer
        , editing
        , anchor_utc
      ) VALUES (
          @layer
        , TRUE
        , @anchor
      )
      ON CONFLICT (hydration_id) DO UPDATE SET
          layer    = EXCLUDED.layer
        , editing  = TRUE;
      """;
    var parameters = new {
      layer,
      anchor = anchor.ToDateTimeUtc(),
    };
    await connection.ExecuteAsync(sql, parameters, cancellationToken);
  }

  // What the row says, inside the caller's transaction if it has one. A database that was not built by the seed tool has
  // no row, so no anchor, and is refused unless the caller gives one to measure from.
  public static async Task<(Instant Anchor, string? Layer, bool Editing)> ReadAsync(
    NpgsqlConnection connection,
    Instant? anchorIfUnrecorded,
    CancellationToken cancellationToken
  ) {
    var sql = """
      SELECT
          h.anchor_utc  AS anchor_utc
        , h.layer       AS layer
        , h.editing     AS editing
      FROM (SELECT) AS _
      JOIN seed.hydration AS h  ON TRUE;
      """;
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken)
      ? (
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc)),
        await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
        reader.GetBoolean(2))
      : anchorIfUnrecorded is { } fallback
        ? (fallback, null, false)
        : throw new SeedFormatException("The database has no seed.hydration row, so it was not built by the seed tool and has no anchor to measure relative times from.");
  }
}
