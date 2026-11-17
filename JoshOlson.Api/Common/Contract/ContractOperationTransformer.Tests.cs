using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace JoshOlson.Api.Common.Contract;

// What the document says about every operation, read from the document the command writes. A new action that breaks
// one of these rules (no summary, no success response, an error that is not problem details) fails here.
public partial class ContractOperationTransformerTests : IAsyncLifetime {
  private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
  private readonly WebApplicationFactory<Program> _factory;
  private JsonDocument _document = null!;

  public ContractOperationTransformerTests(WebApplicationFactory<Program> factory)
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

  [GeneratedRegex(@"^[a-z][A-Za-z0-9]*\.[a-z][A-Za-z0-9]*\z", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
  private static partial Regex ControllerDotAction();

  // Every operation, by its operation ID.
  private Dictionary<string, JsonElement> Operations() {
    var operations = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    foreach (var path in _document.RootElement.GetProperty("paths").EnumerateObject()) {
      foreach (var method in path.Value.EnumerateObject()) {
        if (method.Value.ValueKind is JsonValueKind.Object && method.Value.TryGetProperty("responses", out _)) {
          operations.Add(method.Value.GetProperty("operationId").GetString()!, method.Value);
        }
      }
    }
    return operations;
  }

  private static List<string> Statuses(JsonElement operation)
    => [.. operation.GetProperty("responses").EnumerateObject().Select(response => response.Name)];

  [Fact]
  public void Document_EveryOperation_HasAControllerDotActionID() {
    var ids = Operations().Keys.ToList();

    Assert.NotEmpty(ids);
    Assert.All(ids, id => Assert.Matches(ControllerDotAction(), id));
    Assert.Contains("tag.getAll", ids);
    Assert.Contains("signIn.signIn", ids);
  }

  [Fact]
  public void Document_EveryOperation_HasASummary()
    => Assert.All(
      Operations(),
      operation => Assert.False(
        string.IsNullOrWhiteSpace(operation.Value.GetProperty("summary").GetString()),
        $"{operation.Key} has no summary"));

  [Fact]
  public void Document_EveryOperation_HasASuccessResponse()
    => Assert.All(
      Operations(),
      operation => Assert.True(
        Statuses(operation.Value).Any(status => status.StartsWith('2')),
        $"{operation.Key} documents no 2xx response"));

  [Fact]
  public void Document_EveryError_IsProblemJson()
    => Assert.All(
      Operations(),
      operation => {
        foreach (var response in operation.Value.GetProperty("responses").EnumerateObject()) {
          if (!response.Name.StartsWith('4') || !response.Value.TryGetProperty("content", out var content)) continue;
          Assert.Equal([ProblemContentType.Name], content.EnumerateObject().Select(media => media.Name));
        }
      });

  [Fact]
  public void Document_AnActionThatNeedsASession_Has401AndAnAnonymousOneDoesNot() {
    var operations = Operations();

    Assert.Contains("401", Statuses(operations["tag.create"]));
    Assert.Contains("401", Statuses(operations["passkey.getAll"]));
    Assert.DoesNotContain("401", Statuses(operations["tag.getAll"]));
    Assert.DoesNotContain("401", Statuses(operations["enrollment.options"]));
  }

  [Fact]
  public void Document_AnActionWithABody_Has400AndOneWithoutDoesNot() {
    var operations = Operations();

    Assert.Contains("400", Statuses(operations["tag.create"]));
    Assert.Contains("400", Statuses(operations["signIn.signIn"]));
    Assert.DoesNotContain("400", Statuses(operations["tag.get"]));
    Assert.DoesNotContain("400", Statuses(operations["passkey.getAll"]));
  }

  [Fact]
  public void Document_AWriteThatValidates_Has422WithTheFieldErrors() {
    var tagCreate = Operations()["tag.create"];

    var schema = tagCreate
      .GetProperty("responses").GetProperty("422")
      .GetProperty("content").GetProperty(ProblemContentType.Name)
      .GetProperty("schema").GetProperty("$ref").GetString();

    Assert.EndsWith("/ValidationProblemDetails", schema, StringComparison.Ordinal);
  }

  [Fact]
  public void Document_ActionsThatReturnAnActionResult_StillHaveTheirSuccessBody() {
    var operations = Operations();

    Assert.Contains("200", Statuses(operations["tag.get"]));
    Assert.Contains("200", Statuses(operations["passkey.register"]));
    Assert.Contains("200", Statuses(operations["passkey.remove"]));
    Assert.Equal(["201", "400", "401", "422"], Statuses(operations["tag.create"]).Order(StringComparer.Ordinal));
  }
}
