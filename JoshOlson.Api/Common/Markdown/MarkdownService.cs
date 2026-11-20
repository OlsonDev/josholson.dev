using Ganss.Xss;
using Markdig;

namespace JoshOlson.Api.Common.Markdown;

// Turns the Markdown a person writes (a tag's description, later a blog post) into HTML that is safe to show with
// `{@html}`. Markdig follows CommonMark with the GitHub-style extras; raw HTML inside the Markdown is escaped, and the
// result goes through HtmlSanitizer as well, so a bug in one stage is not the only protection. The Markdown stays
// the source of truth: the HTML is stored beside it and can always be rendered again.
public sealed class MarkdownService {
  private static readonly MarkdownPipeline _Pipeline = new MarkdownPipelineBuilder()
    .DisableHtml()
    .UseEmphasisExtras()
    .UseAutoLinks()
    .UsePipeTables()
    .UseTaskLists()
    .Build();

  public string? ToHtml(string? markdown) {
    if (markdown is null) return null;

    var html = Markdig.Markdown.ToHtml(markdown, _Pipeline);
    // The sanitizer is cheap to build next to rendering, and not shared, so no state outlives a call.
    var sanitizer = new HtmlSanitizer();
    return sanitizer.Sanitize(html).Trim();
  }
}
