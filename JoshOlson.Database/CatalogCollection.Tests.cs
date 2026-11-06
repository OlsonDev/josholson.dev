namespace JoshOlson.Database;

// Test classes that read the dev database's whole catalog, or create and drop schemas in it, share this collection so
// they run one after another. Postgres's `pg_get_indexdef` and the other `pg_get_*` functions read the live catalog and
// not the transaction's snapshot, so an index dropped by one test while another reads fails the read with
// `cache lookup failed`.
internal static class CatalogCollection {
  public const string Name = "dev database catalog";
}
