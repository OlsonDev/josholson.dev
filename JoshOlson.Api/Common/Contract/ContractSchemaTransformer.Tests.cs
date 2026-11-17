using System.Text.Json;
using JoshOlson.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace JoshOlson.Api.Common.Contract;

// What the document says about the types the built-in generator gets wrong, read from the document the command writes.
public class ContractSchemaTransformerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly WebApplicationFactory<Program> _factory;
  private JsonDocument _document = null!;

  public ContractSchemaTransformerTests(WebApplicationFactory<Program> factory)
    => _factory = factory;

  public async ValueTask InitializeAsync() {
    await using var output = new StringWriter();
    _ = await OpenApiCommand.RunAsync(_factory.Services, [], output, _cancellationToken);
    _document = JsonDocument.Parse(output.ToString());
  }

  public ValueTask DisposeAsync() {
    _document.Dispose();
    return ValueTask.CompletedTask;
  }

  private JsonElement Schema(string name)
    => _document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);

  [Fact]
  public void Document_Integer_IsAnIntegerAndNotAStringOfDigits() {
    var tagId = Schema("TagResponse").GetProperty("properties").GetProperty("tagId");

    Assert.Equal("integer", tagId.GetProperty("type").GetString());
    Assert.False(tagId.TryGetProperty("pattern", out _));
  }

  [Fact]
  public void Transform_ALibraryType_IsOpaque() {
    var schema = Schema("CredentialCreateOptions");

    Assert.False(schema.TryGetProperty("type", out _));
    Assert.False(schema.TryGetProperty("properties", out _));
    Assert.True(schema.TryGetProperty("description", out _));
  }

  [Fact]
  public void Document_TheInternalsOfALibraryType_AreNotThere() {
    var schemas = _document.RootElement.GetProperty("components").GetProperty("schemas");

    Assert.False(schemas.TryGetProperty("AuthenticationExtensionsClientInputs", out _));
    Assert.False(schemas.TryGetProperty("PublicKeyCredentialRpEntity", out _));
  }

  [Fact]
  public void Transform_Instant_IsADateTimeString() {
    var instant = Schema("Instant");

    Assert.Equal("string", instant.GetProperty("type").GetString());
    Assert.Equal("date-time", instant.GetProperty("format").GetString());
  }

  [Fact]
  public void Transform_Enum_ListsTheNumbersTheApiSendsAndTheirNames() {
    var schema = Schema("PasskeyTransport");

    Assert.Equal("integer", schema.GetProperty("type").GetString());
    Assert.Equal(
      Enum.GetValues<PasskeyTransport>().Select(transport => (int)transport),
      schema.GetProperty("enum").EnumerateArray().Select(number => number.GetInt32())
    );
    Assert.Equal(
      Enum.GetNames<PasskeyTransport>(),
      schema.GetProperty("x-enum-varnames").EnumerateArray().Select(name => name.GetString())
    );
  }
}
