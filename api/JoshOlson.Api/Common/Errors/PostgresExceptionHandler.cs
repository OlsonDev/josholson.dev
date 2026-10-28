using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace JoshOlson.Api.Common.Errors;

// Maps constraint failures to 4xx ProblemDetails so services and controllers need no try/catch.
// The Postgres message is not sent to the client: it names constraints and values.
public sealed class PostgresExceptionHandler : IExceptionHandler {
  private readonly IProblemDetailsService _problemDetails;

  public PostgresExceptionHandler(IProblemDetailsService problemDetails)
    => _problemDetails = problemDetails;

  public async ValueTask<bool> TryHandleAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken
  ) {
    var (status, title) = exception switch {
      PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation }
        => (StatusCodes.Status409Conflict, "The request conflicts with existing data."),
      PostgresException { SqlState: PostgresErrorCodes.CheckViolation }
        => (StatusCodes.Status400BadRequest, "A value is not valid."),
      _ => (0, "")
    };
    if (status is 0) return false;

    httpContext.Response.StatusCode = status;
    return await _problemDetails.TryWriteAsync(new() {
      HttpContext = httpContext,
      Exception = exception,
      ProblemDetails = { Status = status, Title = title }
    });
  }
}
