using JoshOlson.Database.Layers;
using JoshOlson.Shared.Results;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Seeding;

// Puts a layer's rows into a database that already has the schema: the chain of layers from the root to the named
// layer is folded into its rows (`LayerMerger`), turned into `INSERT`s (`SeedSql`), and run in the order given. The
// caller owns the transaction, so it decides when the rows commit (a bake commits them, a test may roll them back).
// The tables are expected to be empty; a row that collides with one already there fails the insert and the caller's
// transaction with it.
public static class SeedApplier {
  // Returns how many rows went in, or why they did not: the chain does not merge, a value cannot be written, or
  // Postgres refused a row. The caller's transaction is then no use for anything but rolling back.
  public static async Task<Result<int, SeedProblems>> ApplyAsync(
    NpgsqlTransaction transaction,
    LayerTree tree,
    string layer,
    Instant anchor,
    CancellationToken cancellationToken
  ) {
    var merged = LayerMerger.Merge(tree.ChainTo(layer), tree.Schema);
    if (merged is SeedProblems unmerged) {
      return unmerged;
    }
    var built = SeedSql.Build(merged.OrThrow(), tree.Schema, anchor);
    return built is SeedProblems unbuilt
      ? unbuilt
      : await ApplyAsync(transaction, built.OrThrow(), cancellationToken);
  }

  public static async Task<Result<int, SeedProblems>> ApplyAsync(
    NpgsqlTransaction transaction,
    List<SeedStatement> statements,
    CancellationToken cancellationToken
  ) {
    var connection = transaction.Connection
      ?? throw new InvalidOperationException("The transaction has no connection: it was already completed.");
    var rows = 0;
    foreach (var statement in statements) {
      await using var command = new NpgsqlCommand(statement.Sql, connection, transaction);
      foreach (var parameter in statement.Parameters) {
        _ = command.Parameters.Add(parameter);
      }
      try {
        rows += await command.ExecuteNonQueryAsync(cancellationToken);
      } catch (PostgresException exception) {
        return SeedProblems.Of($"Inserting into {statement.Table} failed: {exception.MessageText}");
      }
    }
    return rows;
  }
}
