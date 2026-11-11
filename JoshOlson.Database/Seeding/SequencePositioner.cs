using JoshOlson.Database.Layers;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Database.Seeding;

// Positions the identity sequences after rows with explicit IDs went in. `OVERRIDING SYSTEM VALUE` leaves a sequence
// where it was (ADR 0015, seed ID blocks), so something has to move it past the IDs the seeds took, and there are two
// moments that want it:
// - `ChainAsync`, when a database is finally built for use: every identity sequence, in every table, goes to the start
//   of the next depth's block (`IdBlocks`), or past the largest ID its table has in the whole chain if that is higher.
//   A row created in the database then takes an ID where an export of a child layer expects its new rows, and cannot
//   collide with a seed row or with a row of any layer at the same depth or above.
// - `RootAsync`, while layers apply for a migration: each sequence goes to the largest ID in the root layer alone, so a
//   migration that generates IDs gives the same ones in a parent and in every child (the children's blocks start higher).
// A sequence never moves backwards: rows a migration inserted already advanced it, and those stay behind the new value.
// `setval` is not transactional, so this runs on the connection after the rows are committed.
public static class SequencePositioner {
  // Returns how many sequences moved.
  public static async Task<int> ChainAsync(
    NpgsqlConnection connection,
    LayerTree tree,
    SeedSchema schema,
    string layer,
    CancellationToken cancellationToken
  ) {
    var chain = tree.ChainTo(layer);
    // The chain's length is the next depth: a layer at depth 1 has a chain of two.
    return await SetAsync(
      connection,
      LayerMerger.Merge(chain, schema),
      schema,
      IdBlocks.Start(chain.Count),
      cancellationToken);
  }

  public static async Task<int> RootAsync(
    NpgsqlConnection connection,
    LayerTree tree,
    SeedSchema schema,
    string layer,
    CancellationToken cancellationToken
  )
    => await SetAsync(connection, LayerMerger.Merge([tree.ChainTo(layer)[0]], schema), schema, floor: null, cancellationToken);

  // Without a `floor`, only the tables the state has rows for move, each to its largest ID. With one, every identity
  // column in the schema moves, to the larger of its table's largest ID and the ID just before the floor.
  public static async Task<int> SetAsync(
    NpgsqlConnection connection,
    SeedState state,
    SeedSchema schema,
    long? floor,
    CancellationToken cancellationToken
  ) {
    var moved = 0;
    foreach (var table in schema.InsertOrder()) {
      var hasRows = state.Tables.TryGetValue(table.Table.Name, out var rows) && rows.Count > 0;
      if (!hasRows && floor is null) {
        continue;
      }
      foreach (var column in table.Table.Columns) {
        if (column.GeneratedAs is not (GeneratedAs.IdentityAlways or GeneratedAs.IdentityByDefault)) {
          continue;
        }
        var largest = hasRows
          ? rows!.Values
            .Select(row => row.GetValueOrDefault(column.Name))
            .OfType<SeedValue.Integer>()
            .Select(value => value.Value)
            .DefaultIfEmpty(long.MinValue)
            .Max()
          : long.MinValue;
        if (floor is { } start) {
          largest = Math.Max(largest, start - 1);
        }
        if (largest is long.MinValue) {
          continue;
        }
        if (await MoveAsync(connection, table.Table.Name, column.Name, largest, cancellationToken)) {
          moved++;
        }
      }
    }
    return moved;
  }

  private static async Task<bool> MoveAsync(
    NpgsqlConnection connection,
    string table,
    string column,
    long largest,
    CancellationToken cancellationToken
  ) {
    var sequence = await connection.QuerySingleAsync<string>(
      "SELECT pg_get_serial_sequence(@table, @column);",
      new { table, column },
      cancellationToken);
    var current = await connection.QuerySingleAsync<long?>(
      "SELECT pg_sequence_last_value(@sequence::regclass);",
      new { sequence },
      cancellationToken);
    if (current >= largest) {
      return false;
    }
    _ = await connection.QuerySingleAsync<long>(
      "SELECT setval(@sequence::regclass, @largest, TRUE);",
      new { sequence, largest },
      cancellationToken);
    return true;
  }
}
