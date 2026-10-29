using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace JoshOlson.Api.Common.Errors;

public class PostgresExceptionHandlerTests {
  private static async Task<(bool Handled, int Status)> HandleAsync(Exception exception) {
    await using var provider = new ServiceCollection().AddOptions().AddProblemDetails().BuildServiceProvider();
    var handler = new PostgresExceptionHandler(provider.GetRequiredService<IProblemDetailsService>());
    var httpContext = new DefaultHttpContext { RequestServices = provider };

    var handled = await handler.TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

    return (handled, httpContext.Response.StatusCode);
  }

  private static PostgresException Violation(string sqlState)
    => new("foo", "ERROR", "ERROR", sqlState);

  [Theory]
  [InlineData(PostgresErrorCodes.UniqueViolation, 409)]
  [InlineData(PostgresErrorCodes.ForeignKeyViolation, 409)]
  [InlineData(PostgresErrorCodes.CheckViolation, 422)]
  public async Task TryHandleAsync_ConstraintViolation_MapsToStatus(string sqlState, int status)
    => Assert.Equal((true, status), await HandleAsync(Violation(sqlState)));

  [Fact]
  public async Task TryHandleAsync_OtherPostgresError_IsNotHandled() {
    var (handled, _) = await HandleAsync(Violation(PostgresErrorCodes.DeadlockDetected));

    Assert.False(handled);
  }

  [Fact]
  public async Task TryHandleAsync_OtherException_IsNotHandled() {
    var (handled, _) = await HandleAsync(new InvalidOperationException());

    Assert.False(handled);
  }
}
