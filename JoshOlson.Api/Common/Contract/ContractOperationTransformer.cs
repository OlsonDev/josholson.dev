using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace JoshOlson.Api.Common.Contract;

// Names each operation `controller.action` in camelCase (`tag.getAll`), the names the TypeScript client is generated
// from, and adds the two responses a controller does not write on every action: a `401` where a session is needed (the
// global `SessionFilter`), and a `400` where there is a body (one that is not JSON, or does not fit its type). The
// statuses an action chooses for itself are on the action, as `[ProducesResponseType]` and `[ProblemResponse]`.
public sealed class ContractOperationTransformer : IOpenApiOperationTransformer {
  public async Task TransformAsync(
    OpenApiOperation operation,
    OpenApiOperationTransformerContext context,
    CancellationToken cancellationToken
  ) {
    if (context.Description.ActionDescriptor is not ControllerActionDescriptor action) return;

    operation.OperationId = $"{ToCamelCase(action.ControllerName)}.{ToCamelCase(action.ActionName)}";
    operation.Responses ??= [];

    var needsSession = !action.EndpointMetadata.OfType<IAllowAnonymous>().Any();
    if (needsSession && !operation.Responses.ContainsKey("401")) {
      operation.Responses["401"] = await ProblemAsync(context, "There is no signed-in session.", cancellationToken);
    }

    var hasBody = context.Description.ParameterDescriptions.Any(parameter => parameter.Source == BindingSource.Body);
    if (hasBody && !operation.Responses.ContainsKey("400")) {
      operation.Responses["400"] = await ProblemAsync(
        context,
        "The body is not valid JSON, or does not fit the type.",
        cancellationToken);
    }
  }

  private static string ToCamelCase(string name)
    => JsonNamingPolicy.CamelCase.ConvertName(name);

  private static async Task<OpenApiResponse> ProblemAsync(
    OpenApiOperationTransformerContext context,
    string description,
    CancellationToken cancellationToken
  ) {
    var schema = await context.GetOrCreateSchemaAsync(
      typeof(ProblemDetails),
      parameterDescription: null,
      cancellationToken);
    return new() {
      Description = description,
      Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal) {
        [ProblemContentType.Name] = new OpenApiMediaType { Schema = schema },
      },
    };
  }
}
