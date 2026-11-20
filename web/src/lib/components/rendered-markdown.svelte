<script lang="ts">
  import { cn } from '#lib/utils.ts';

  // The HTML of a Markdown text (a tag's description, later a post), which the API has rendered and sanitized when the text
  // was written, so `{@html}` is fine here and nowhere else. The text is not ours to put classes on, so the styles reach it
  // with `:global`, scoped by the wrapper; they ship only with the pages that show some. Used by: `Tag` (its hover card).
  let { html, class: className }: { html: string; class?: string } = $props();
</script>

<div class={cn('markdown', className)}>{@html html}</div>

<style>
  .markdown {
    > :global(:first-child) {
      margin-top: 0;
    }
    > :global(:last-child) {
      margin-bottom: 0;
    }
    :global(:is(p, ul, ol, pre, blockquote, table)) {
      margin-block: 0.5rem;
    }
    :global(ul) {
      list-style: disc;
      padding-left: 1.25rem;
    }
    :global(ol) {
      list-style: decimal;
      padding-left: 1.25rem;
    }
    :global(a) {
      text-decoration: underline;
      text-underline-offset: 4px;
    }
    :global(code) {
      border-radius: 0.25rem;
      background: var(--muted);
      padding: 0.1rem 0.3rem;
      font-size: 0.85em;
    }
    :global(pre) {
      overflow-x: auto;
      border-radius: 0.5rem;
      background: var(--muted);
      padding: 0.75rem;

      :global(code) {
        background: none;
        padding: 0;
      }
    }
    :global(blockquote) {
      border-left: 3px solid var(--border);
      padding-left: 0.75rem;
      color: var(--muted-foreground);
    }
    :global(:is(h1, h2, h3)) {
      margin-block: 0.75rem 0.25rem;
      font-weight: 600;
    }
  }
</style>
