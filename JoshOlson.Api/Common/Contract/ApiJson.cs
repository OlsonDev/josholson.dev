using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace JoshOlson.Api.Common.Contract;

// The JSON settings of the contract, applied to MVC (what the API sends and reads) and to the options the OpenAPI
// generator reads (what the document describes), so the two cannot differ (ADR 0026, REST and JSON with controllers).
// A number is a number: the web defaults also read `"5"` as an int, and a number sent as a string is a client's mistake,
// so it is a `400` (and the generator then describes the field as an integer, not "an integer or a string of digits").
// An enum is its number, as System.Text.Json writes it by default (ADR 0012, enum-like columns as SMALLINT). A converter
// of our own would hide the enum from the OpenAPI generator, which only describes the built-in ones.
public static class ApiJson {
  public static JsonSerializerOptions Configure(JsonSerializerOptions options) {
    options.NumberHandling = JsonNumberHandling.Strict;
    return options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
  }
}
