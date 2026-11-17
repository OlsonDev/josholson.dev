using System.Text.Json;
using JoshOlson.Shared.IO;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace JoshOlson.Api.Common.Contract;

public class OpenApiCommandTests {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly WebApplicationFactory<Program> _factory;

  public OpenApiCommandTests(WebApplicationFactory<Program> factory)
    => _factory = factory;

  [Fact]
  public async Task Run_WithoutAPath_WritesTheDocumentToTheOutput() {
    await using var output = new StringWriter();

    var exitCode = await OpenApiCommand.RunAsync(_factory.Services, [], output, _cancellationToken);

    Assert.Equal(0, exitCode);
    using var document = JsonDocument.Parse(output.ToString());
    Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/tags", out _));
  }

  [Fact]
  public async Task Run_WithAPath_WritesTheDocumentThereAndNothingToTheOutput() {
    var directory = TempDirectory.Create("openapi");
    try {
      var path = Path.Combine(directory, "openapi.json");
      await using var output = new StringWriter();

      var exitCode = await OpenApiCommand.RunAsync(_factory.Services, [path], output, _cancellationToken);

      Assert.Equal(0, exitCode);
      Assert.Equal("", output.ToString());
      var text = await File.ReadAllTextAsync(path, _cancellationToken);
      using var document = JsonDocument.Parse(text);
      Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/tags", out _));
      Assert.EndsWith("\n", text, StringComparison.Ordinal);
    } finally {
      Directory.Delete(directory, recursive: true);
    }
  }
}
