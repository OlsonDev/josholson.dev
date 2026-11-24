#if INCLUDE_TESTS
using Npgsql;

namespace JoshOlson.Api.Common.Db;

// The database the current request runs on, when its test scope names one (ADR 0033, a scoped database per test).
// `ScopedDatabaseMiddleware` sets it, and the request's connection is opened from it; null is the default database.
public sealed class ScopedDatabase {
  public NpgsqlDataSource? DataSource { get; set; }
}
#endif
