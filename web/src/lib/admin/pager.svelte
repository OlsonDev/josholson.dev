<script lang="ts">
  import ChevronLeftIcon from '@lucide/svelte/icons/chevron-left';
  import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';

  import { Button } from '#lib/components/ui/button/index.ts';

  import { type Paging, visiblePages } from './list-state.ts';

  // The page links of a list, with a chevron at each end (named for a screen reader: "Previous page", "Next page"). All of
  // them are flat, with a border the color of the page, and none is smaller than the 2rem square a chevron is, so no edge shows on
  // one and not another. Every page is a real link (`href(page, size)` is the address of one), so back, refresh, and a
  // copied address all land on the page that was showing. Nothing in it moves when a page is chosen: the same number of
  // places every time (`visiblePages`), digits all one width (tabular figures), and every place wide enough for the
  // longest page number, which depends on how many pages there are and not on which one is showing. Used by: tags.
  let { paging, href }: { paging: Paging; href: (page: number, size: number) => string } = $props();

  const digits = $derived(paging.pageCount.toString().length);
</script>

{#if paging.pageCount > 1}
  <nav aria-label="Pages" class="flex items-center gap-1 tabular-nums" style="--page-digits: {digits}">
    <Button
      variant="ghost"
      size="icon-sm"
      href={paging.page > 1 ? href(paging.page - 1, paging.size) : undefined}
      disabled={paging.page <= 1}
      aria-label="Previous page"
    >
      <ChevronLeftIcon aria-hidden="true" />
    </Button>
    {#each visiblePages(paging.page, paging.pageCount) as shown, index (index)}
      {#if shown === 'gap'}
        <span
          class="inline-flex min-w-[max(2rem,calc(var(--page-digits)*1ch+1.25rem))] justify-center text-sm text-muted-foreground"
          aria-hidden="true"
        >
          …
        </span>
      {:else}
        <Button
          variant={shown === paging.page ? 'secondary' : 'ghost'}
          size="sm"
          class="min-w-[max(2rem,calc(var(--page-digits)*1ch+1.25rem))]"
          href={href(shown, paging.size)}
          aria-label="Page {shown}"
          aria-current={shown === paging.page ? 'page' : undefined}
        >
          {shown}
        </Button>
      {/if}
    {/each}
    <Button
      variant="ghost"
      size="icon-sm"
      href={paging.page < paging.pageCount ? href(paging.page + 1, paging.size) : undefined}
      disabled={paging.page >= paging.pageCount}
      aria-label="Next page"
    >
      <ChevronRightIcon aria-hidden="true" />
    </Button>
  </nav>
{/if}
