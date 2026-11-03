using System.Text.Json;
using System.Text.Json.Serialization;
using JoshOlson.Shared.Extensions;

namespace JoshOlson.Api.Common.Binding;

// Every string in a request body arrives trimmed, and text that is only whitespace arrives as null, the form the
// database's nullable text columns take (a value or NULL, never ''). A required field then fails `[Required]`, not a
// pattern, and `[Trimmed]` stays as the check for code that builds a payload without JSON. Writing is unchanged.
public sealed class TrimmingStringJsonConverter : JsonConverter<string> {
  public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    => reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName
      ? reader.GetString().TrimToNull()
      : throw new JsonException($"Expected a string but found {reader.TokenType}.");

  public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    => writer.WriteStringValue(value);
}
