using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NodaTime;

namespace JoshOlson.Api.Common.Contract;

// Makes the OpenAPI schema say what the JSON actually is, so the TypeScript generated from it is not wider than the
// wire. The built-in generator describes an `Instant` as an empty object (NodaTime's converter writes an ISO 8601
// string) and an enum as any integer. An enum lists its numbers and, in `x-enum-varnames`, the names the TypeScript
// generator turns into members. (It also describes every integer as "an integer or a string of digits" while the reader
// accepts both, which `ApiJson` ends by reading numbers strictly.)
//
// A type that is neither ours, the framework's, nor NodaTime's (the WebAuthn types of the Fido2 library) is opaque: the
// web relays it between the API and the browser and never reads inside it, so its members are not part of the contract
// and nothing is generated for them.
public sealed class ContractSchemaTransformer : IOpenApiSchemaTransformer {
  private static readonly string[] _FrameworkAssemblyPrefixes = [
    "System",
    "Microsoft",
    "NodaTime",
  ];

  public Task TransformAsync(
    OpenApiSchema schema,
    OpenApiSchemaTransformerContext context,
    CancellationToken cancellationToken
  ) {
    var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
    if (type == typeof(Instant)) {
      schema.Type = JsonSchemaType.String;
      schema.Format = "date-time";
    } else if (type.IsEnum && type.Assembly == typeof(ContractSchemaTransformer).Assembly) {
      DescribeEnum(schema, type);
    } else if (IsLibraryType(type)) {
      MakeOpaque(schema);
    }
    return Task.CompletedTask;
  }

  private static bool IsLibraryType(Type type) {
    var assemblyName = type.Assembly.GetName().Name ?? "";
    return !type.IsPrimitive
      && type.Assembly != typeof(ContractSchemaTransformer).Assembly
      && !_FrameworkAssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));
  }

  private static void MakeOpaque(OpenApiSchema schema) {
    schema.Type = null;
    schema.Format = null;
    schema.Pattern = null;
    schema.Properties?.Clear();
    schema.Required?.Clear();
    schema.Items = null;
    schema.AdditionalProperties = null;
    schema.AllOf?.Clear();
    schema.AnyOf?.Clear();
    schema.OneOf?.Clear();
    schema.Enum?.Clear();
    schema.Description = "A library type the web relays and never reads, so its members are not part of the contract.";
  }

  private static void DescribeEnum(OpenApiSchema schema, Type type) {
    var names = new JsonArray();
    var numbers = new List<JsonNode>();
    foreach (var name in Enum.GetNames(type)) {
      names.Add(name);
      numbers.Add(JsonValue.Create(Convert.ToInt64(Enum.Parse(type, name), CultureInfo.InvariantCulture)));
    }
    schema.Type = JsonSchemaType.Integer;
    schema.Pattern = null;
    schema.Enum = numbers;
    schema.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
    schema.Extensions["x-enum-varnames"] = new JsonNodeExtension(names);
  }
}
