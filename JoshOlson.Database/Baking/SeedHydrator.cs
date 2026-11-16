using JoshOlson.Database.Layers;
using JoshOlson.Database.Seeding;
using JoshOlson.Shared.Data;
using JoshOlson.Shared.Results;
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

  // Returns how many rows the layer chain put in, or why it could not be (`SeedApplier.ApplyAsync`); nothing is then
  // committed or recorded.
  public static async Task<Result<int, SeedProblems>> HydrateAsync(
    string database,
    LayerTree tree,
    string layer,
    bool editing,
    Instant anchor,
    CancellationToken cancellationToken
  )
    => await FillAsync(database, tree, layer, editing, anchor, forMigration: false, cancellationToken);

  // The same for `seeds:migrate`, which puts a layer's chain into a database at the schema it was written for before the
  // newer migrations run over it. Two things differ. Foreign keys are not enforced while the rows go in (`session_replication_role`
  // is `replica`, which the owner may set): a row can point at reference data from a repeatable migration that is written for
  // a newer schema and so could not be applied here, and the rows are checked against the real constraints afterwards, at
  // the newest version. And the sequences are positioned by the root layer alone (`SequencePositioner.RootAsync`), so a
  // migration that generates IDs gives a parent and every child the same ones.
  public static async Task<Result<int, SeedProblems>> HydrateForMigrationAsync(
    string database,
    LayerTree tree,
    string layer,
    Instant anchor,
    CancellationToken cancellationToken
  )
    => await FillAsync(database, tree, layer, editing: false, anchor, forMigration: true, cancellationToken);

  // Whether each of `layers` builds: its chain is put into a clone of `template` (the schema template the tree is
  // written for), which is then dropped. The merge of a chain is in memory and Postgres has not seen the rows, so a
  // foreign key to a row that is gone, a duplicate of a unique value, or a check a value fails shows only here.
  // Returns one problem for each layer that does not build, worded by `problem` from the layer's name and the error.
  public static async Task<List<string>> BuildProblemsAsync(
    ServerDatabases databases,
    NpgsqlConnection owner,
    string template,
    string purpose,
    LayerTree tree,
    List<string> layers,
    Instant anchor,
    Func<string, string, string> problem,
    CancellationToken cancellationToken
  ) {
    var problems = new List<string>();
    foreach (var layer in layers) {
      await using var scratch = await databases.CloneScratchAsync(owner, template, purpose, cancellationToken);
      if (await HydrateAsync(scratch.Name, tree, layer, editing: false, anchor, cancellationToken) is SeedProblems unbuilt) {
        problems.Add(problem(layer, unbuilt.ToString()));
      }
    }
    return problems;
  }

  // Both of the above. `forMigration` is the two things that differ: foreign keys are not enforced while the rows go
  // in, and the sequences are positioned by the root layer alone.
  private static async Task<Result<int, SeedProblems>> FillAsync(
    string database,
    LayerTree tree,
    string layer,
    bool editing,
    Instant anchor,
    bool forMigration,
    CancellationToken cancellationToken
  ) {
    await using var connection = await DatabaseEnvironment.OpenOwnerAsync(database, cancellationToken);
    int rows;
    await using (var transaction = await connection.BeginTransactionAsync(cancellationToken)) {
      if (forMigration) {
        await connection.ExecuteAsync("SET LOCAL session_replication_role = replica;", cancellationToken);
      }
      var applied = await SeedApplier.ApplyAsync(transaction, tree, layer, anchor, cancellationToken);
      if (applied is SeedProblems refused) {
        return refused;
      }
      rows = applied.OrThrow();
      await transaction.CommitAsync(cancellationToken);
    }
    await SeedHydration.RecordAsync(connection, new(anchor, layer, editing), cancellationToken);
    _ = forMigration
      ? await SequencePositioner.RootAsync(connection, tree, layer, cancellationToken)
      : await SequencePositioner.ChainAsync(connection, tree, layer, editing, cancellationToken);
    return rows;
  }
}
