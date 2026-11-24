#if INCLUDE_TESTS
namespace JoshOlson.Api.Common.Db;

// Points a request at the database its test scope names (ADR 0033, a scoped database per test). A request with no scope
// header runs on the default database, so a readiness check and a test that has not moved to its own database still
// work. A header that names no database is refused, never sent to the default one: a test that is meant to be alone
// must not write into shared data because it mistyped an ID or ran after its database was dropped.
public sealed class ScopedDatabaseMiddleware {
  // The header a test sends. A custom name, so it never collides with one a proxy or the browser sets.
  public const string HeaderName = "X-Test-Scope";

  private readonly RequestDelegate _next;

  public ScopedDatabaseMiddleware(RequestDelegate next)
    => _next = next;

  public async Task InvokeAsync(HttpContext context, ScopedDatabaseRegistry registry, ScopedDatabase scopedDatabase) {
    if (!context.Request.Headers.TryGetValue(HeaderName, out var values)) {
      await _next(context);
      return;
    }

    var dataSource = await registry.FindAsync(values.ToString(), context.RequestAborted);
    if (dataSource is null) {
      var problem = Results.Problem("That test scope has no database.", statusCode: StatusCodes.Status400BadRequest);
      await problem.ExecuteAsync(context);
      return;
    }
    scopedDatabase.DataSource = dataSource;
    await _next(context);
  }
}
#endif
