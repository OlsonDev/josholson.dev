using System.Text.Json;
using Fido2NetLib.Objects;
using JoshOlson.Api.Auth;
using NodaTime;
using Xunit;

namespace JoshOlson.Api.Common.Contract;

public class ApiJsonTests {
  private static readonly JsonSerializerOptions _Options = ApiJson.Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

  [Fact]
  public void Serialize_Instant_IsAnIsoString()
    => Assert.Equal("\"2026-01-02T03:04:05Z\"", JsonSerializer.Serialize(Instant.FromUtc(2026, 1, 2, 3, 4, 5), _Options));

  [Theory]
  [InlineData("\"5\"")]
  [InlineData("\"\"")]
  [InlineData("5.5")]
  public void Deserialize_AnIntNotWrittenAsAWholeNumber_IsRefused(string json)
    => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<int>(json, _Options));

  [Fact]
  public void Deserialize_AnInt_IsTheNumber()
    => Assert.Equal(5, JsonSerializer.Deserialize<int>("5", _Options));

  [Fact]
  public void Serialize_Enum_IsItsNumber()
    => Assert.Equal("6", JsonSerializer.Serialize(PasskeyTransport.SmartCard, _Options));

  [Fact]
  public void Serialize_ALibraryEnum_KeepsTheLibrarysFormat()
    => Assert.Equal("-7", JsonSerializer.Serialize(COSE.Algorithm.ES256, _Options));

  [Fact]
  public void Deserialize_Instant_IsTheMoment()
    => Assert.Equal(Instant.FromUtc(2026, 1, 2, 3, 4, 5), JsonSerializer.Deserialize<Instant>("\"2026-01-02T03:04:05Z\"", _Options));
}
