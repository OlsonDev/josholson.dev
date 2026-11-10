using JoshOlson.Database.Layers;
using NodaTime;
using Npgsql;

namespace JoshOlson.Database.Seeding;

// Puts a layer's rows into a database that already has the schema: the chain of layers from the root to the named
// layer is folded into its rows (`LayerMerger`), turned into `INSERT`s (`SeedSql`), and run in the order given. The
// caller owns the transaction, so it decides when the rows commit (a bake commits them, a test may roll them back).
// The tables are expected to be empty; a row that collides with one already there fails the insert and the caller's
// transaction with it.
public static class SeedApplier {
  // Returns how many rows went in.
  public static async Task<int> ApplyAsync(
    NpgsqlTransaction transaction,
    LayerTree tree,
    SeedSchema schema,
    string layer,
    Instant anchor,
    CancellationToken cancellationToken
  ) {
    var state = LayerMerger.Merge(tree.ChainTo(layer), schema);
    var statements = SeedSql.Build(state, schema, anchor);
    return await ApplyAsync(transaction, statements, cancellationToken);
  }

  public static async Task<int> ApplyAsync(
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
        throw new SeedFormatException($"Inserting into {statement.Table} failed: {exception.MessageText}", exception);
      }
    }
    return rows;
  }
}
