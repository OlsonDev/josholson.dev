using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JoshOlson.Database.Layers;

// The one text a `jsonb` value has, so the same value reads as equal whether it came from a layer file or from the database
// (ADR 0035, typed seed values). It is compact, with the keys of every object in the order Postgres stores them: by length
// in bytes first, then bytewise, and the last of a repeated key kept. Numbers keep the digits they were written with.
internal static class JsonCanonical {
  private static readonly JsonWriterOptions _Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
  private static readonly Comparer<string> _Bytewise = Comparer<string>.Create(
    (left, right) => Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right)));

  public static string Normalize(string json) {
    using var document = JsonDocument.Parse(json);
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, _Options)) {
      Write(writer, document.RootElement);
    }
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  private static void Write(Utf8JsonWriter writer, JsonElement element) {
    switch (element.ValueKind) {
      case JsonValueKind.Object:
        writer.WriteStartObject();
        foreach (var property in InStorageOrder(element)) {
          writer.WritePropertyName(property.Name);
          Write(writer, property.Value);
        }
        writer.WriteEndObject();
        break;
      case JsonValueKind.Array:
        writer.WriteStartArray();
        foreach (var item in element.EnumerateArray()) {
          Write(writer, item);
        }
        writer.WriteEndArray();
        break;
      default:
        element.WriteTo(writer);
        break;
    }
  }

  private static List<JsonProperty> InStorageOrder(JsonElement element) {
    var last = new Dictionary<string, JsonProperty>(StringComparer.Ordinal);
    foreach (var property in element.EnumerateObject()) {
      last[property.Name] = property;
    }
    return [.. last.Values.OrderBy(property => Encoding.UTF8.GetByteCount(property.Name)).ThenBy(property => property.Name, _Bytewise)];
  }
}
