using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace JoshOlson.Api.Common.Contract;

// `JoshOlson.Api openapi [path]` (ADR 0026, the API contract): writes the OpenAPI document `web/` generates its
// TypeScript types from, to the path or to standard output. It builds the host but never starts it, so no database is
// needed, only the settings the host reads at startup.
public static class OpenApiCommand {
  public const string Name = "openapi";
  public const string DocumentName = "v1";

  public static async Task<int> RunAsync(
    IServiceProvider services,
    string[] arguments,
    TextWriter output,
    CancellationToken cancellationToken
  ) {
    var provider = services.GetRequiredKeyedService<IOpenApiDocumentProvider>(DocumentName);
    var document = await provider.GetOpenApiDocumentAsync(cancellationToken);
    var json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken);
    if (arguments.FirstOrDefault() is { } path) await File.WriteAllTextAsync(path, json + "\n", cancellationToken);
    else await output.WriteLineAsync(json.AsMemory(), cancellationToken);
    return 0;
  }
}
