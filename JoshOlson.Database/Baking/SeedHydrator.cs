using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Data;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Baking;

// Fills a database that has the schema (a clone of the schema template) with a layer's rows, the one way every seeded
// database is made: a layer template, the dev database `seed <layer>` rebuilds, and the checks `seeds:migrate` runs.
// The chain's rows go in one transaction, the anchor its relative times were worked out from is recorded in
// `seed.hydration` with the layer's name (the clock a test starts at, what an export measures from, and the parent it
// defaults to), and then each identity sequence
// moves past the IDs the chain took.
public static class SeedHydrator {
  // Postgres keeps microseconds, so the anchor is cut to one: it is read back equal to what was written.
  public static Instant Anchor(IClock clock)
    => Instant.FromUnixTimeTicks(clock.GetCurrentInstant().ToUnixTimeTicks() / 10 * 10);

  // Returns how many rows the layer chain put in.
  public static async Task<int> HydrateAsync(
    string database,
    LayerTree tree,
    SeedSchema schema,
    string layer,
    bool editing,
    Instant anchor,
    CancellationToken cancellationToken
  ) {
    await using var connection = new NpgsqlConnection(DatabaseEnvironment.OwnerConnectionString(database));
    await connection.OpenAsync(cancellationToken);
    int rows;
    await using (var transaction = await connection.BeginTransactionAsync(cancellationToken)) {
      rows = await SeedApplier.ApplyAsync(transaction, tree, schema, layer, anchor, cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }
    // A `DateTime` in UTC, because this connection has no NodaTime plugin to write an `Instant` with.
    await connection.ExecuteAsync(
      "INSERT INTO seed.hydration (layer, editing, anchor_utc) VALUES (@layer, @editing, @anchor);",
      new {
        layer,
        editing,
        anchor = anchor.ToDateTimeUtc(),
      },
      cancellationToken);
    _ = await SequencePositioner.ChainAsync(connection, tree, schema, layer, editing, cancellationToken);
    return rows;
  }
}
