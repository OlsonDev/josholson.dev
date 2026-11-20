using Xunit;

namespace JoshOlson.Api.Common.Markdown;

public class MarkdownServiceTests {
  private readonly MarkdownService _markdownService;

  public MarkdownServiceTests(MarkdownService markdownService)
    => _markdownService = markdownService;

  [Fact]
  public void ToHtml_Null_ReturnsNull()
    => Assert.Null(_markdownService.ToHtml(markdown: null));

  [Fact]
  public void ToHtml_RendersCommonMark()
    => Assert.Equal("<p>A <strong>foo</strong> and <em>bar</em>.</p>", _markdownService.ToHtml("A **foo** and *bar*."));

  [Fact]
  public void ToHtml_ScriptAndEventHandlers_AreNeverRendered() {
    var html = _markdownService.ToHtml("<script>alert(1)</script> <img src=x onerror=alert(1)> [baz](javascript:alert(1))");

    Assert.NotNull(html);
    Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ToHtml_Link_IsKept()
    => Assert.Contains("href=\"https://example.com/\"", _markdownService.ToHtml("[foo](https://example.com/)"));
}
