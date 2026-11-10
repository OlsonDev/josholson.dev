using JoshOlson.Database.Layers;
using JoshOlson.Database.Schema;
using JoshOlson.Shared.Data;
using Npgsql;

namespace JoshOlson.Database.Seeding;

// Positions the identity sequences after rows with explicit ids went in. `OVERRIDING SYSTEM VALUE` leaves a sequence
// where it was (ADR 0015, seed id blocks), so something has to move it past the ids the seeds took, and there are two
// moments that want it:
// - `ChainAsync`, when a database is finally built for use: each sequence goes to the largest id its table has in the
//   whole chain, so the next generated id cannot collide with a seed row.
// - `RootAsync`, while layers apply for a migration: each sequence goes to the largest id in the root layer alone, so a
//   migration that generates ids gives the same ones in a parent and in every child (the children's blocks start higher).
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
  )
    => await SetAsync(connection, LayerMerger.Merge(tree.ChainTo(layer), schema), schema, cancellationToken);

  public static async Task<int> RootAsync(
    NpgsqlConnection connection,
    LayerTree tree,
    SeedSchema schema,
    string layer,
    CancellationToken cancellationToken
  )
    => await SetAsync(connection, LayerMerger.Merge([tree.ChainTo(layer)[0]], schema), schema, cancellationToken);

  public static async Task<int> SetAsync(
    NpgsqlConnection connection,
    SeedState state,
    SeedSchema schema,
    CancellationToken cancellationToken
  ) {
    var moved = 0;
    foreach (var table in schema.InsertOrder()) {
      if (!state.Tables.TryGetValue(table.Table.Name, out var rows) || rows.Count is 0) {
        continue;
      }
      foreach (var column in table.Table.Columns) {
        if (column.GeneratedAs is not (GeneratedAs.IdentityAlways or GeneratedAs.IdentityByDefault)) {
          continue;
        }
        var largest = rows.Values
          .Select(row => row.GetValueOrDefault(column.Name))
          .OfType<SeedValue.Integer>()
          .Select(value => value.Value)
          .DefaultIfEmpty(long.MinValue)
          .Max();
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
