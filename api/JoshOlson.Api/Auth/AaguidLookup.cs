using System.Text.Json.Serialization;
using Flurl.Http;

namespace JoshOlson.Api.Auth;

// Names an authenticator model from its AAGUID, for the error message when one is reported (ADR 0031, no AAGUID
// column). Best effort: any failure returns null and never decides anything.
public sealed class AaguidLookup {
  private const string _CommunityListUrl
    = "https://raw.githubusercontent.com/passkeydeveloper/passkey-authenticator-aaguids/main/combined_aaguid.json";

  public async Task<string?> FindNameAsync(Guid aaguid, CancellationToken cancellationToken) {
    try {
      var models = await _CommunityListUrl
        .WithTimeout(TimeSpan.FromSeconds(3))
        .GetJsonAsync<Dictionary<string, Model>>(cancellationToken: cancellationToken);
      return models.GetValueOrDefault(aaguid.ToString())?.Name;
    } catch (FlurlHttpException) {
      // A failed request, a timeout, or a body that is not the expected JSON.
      return null;
    }
  }

  private sealed record Model {
    [JsonPropertyName("name")]
    public string? Name { get; init; }
  }
}
