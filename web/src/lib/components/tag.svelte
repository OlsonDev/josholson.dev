<script lang="ts">
  import RenderedMarkdown from '#lib/components/rendered-markdown.svelte';
  import * as HoverCard from '#lib/components/ui/hover-card/index.ts';
  import { tagStyle } from '#lib/tags/tag-style.ts';
  import { cn } from '#lib/utils.ts';

  // A tag as a colored chip, the same on the public pages and in the admin. The color is the tag's light-mode background and
  // dark mode paints its mirror (`tagColors`). A tag with a description shows it, rendered, in a card when the chip is
  // hovered or focused (`RenderedMarkdown`). A tag with no color is a plain outlined chip, and one with an `href` is a link. Used by: the home page, the tags list.
  let {
    tag,
    href,
    class: className,
  }: {
    tag: { name: string; color: string | null; descriptionHtml?: string | null };
    href?: string;
    class?: string;
  } = $props();

  const style = $derived(tagStyle(tag.color));
  const classes = $derived(
    cn(
      'inline-flex max-w-full items-center rounded-full border px-3 py-1 text-sm font-medium whitespace-nowrap focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none',
      tag.color !== null &&
        'border-transparent bg-(--tag-bg) text-(--tag-fg) dark:bg-(--tag-bg-dark) dark:text-(--tag-fg-dark)',
      className,
    ),
  );
</script>

<!-- The chip itself. The hover card hands it the props that make it its trigger. -->
{#snippet chip(props: Record<string, unknown> = {})}
  {#if href !== undefined}
    <!-- The hover card trigger says role="button", and this is a link. -->
    <!-- svelte-ignore a11y_no_redundant_roles -->
    <a {...props} role="link" {href} {style} class={classes}>{tag.name}</a>
  {:else if 'data-state' in props}
    <button {...props} {style} type="button" class={cn(classes, 'cursor-default')}>{tag.name}</button>
  {:else}
    <span {...props} {style} class={classes}>{tag.name}</span>
  {/if}
{/snippet}

{#if tag.descriptionHtml}
  <HoverCard.Root openDelay={150} closeDelay={100}>
    <HoverCard.Trigger>
      {#snippet child({ props })}
        {@render chip(props)}
      {/snippet}
    </HoverCard.Trigger>
    <HoverCard.Content class="w-80">
      <RenderedMarkdown html={tag.descriptionHtml} />
    </HoverCard.Content>
  </HoverCard.Root>
{:else}
  {@render chip()}
{/if}
