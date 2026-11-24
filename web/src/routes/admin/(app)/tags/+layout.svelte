<script lang="ts">
  import { page } from '$app/state';

  import ListBar from '#lib/admin/list-bar.svelte';
  import ListPane from '#lib/admin/list-pane.svelte';
  import { pagingSearch, parsePaging } from '#lib/admin/list-state.ts';
  import PageSize from '#lib/admin/page-size.svelte';
  import PagerSummary from '#lib/admin/pager-summary.svelte';
  import Pager from '#lib/admin/pager.svelte';
  import PageHeader from '#lib/components/page-header.svelte';
  import { Button } from '#lib/components/ui/button/index.ts';
  import { listHref, paneHref } from '#lib/tags/routes.ts';
  import TagList from '#lib/tags/tag-list.svelte';

  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  // A pane's URL is /admin/tags/5 or /admin/tags/+, which has a `tagId`. The list's own has none. The list's page is in
  // the query (`?page=2&size=25`), and a pane's address carries the same query, so the list behind it stays where it was.
  const open = $derived(page.params['tagId'] !== undefined);
  const paging = $derived(parsePaging(page.url.searchParams, data.tags.length));
  const search = $derived(pagingSearch(paging));
  const rows = $derived(data.tags.slice(paging.start, paging.end));

  // A new page starts at the top of the list.
  $effect(() => {
    void paging.page;
    document.querySelector('[data-list-scroll]')?.scrollTo({ top: 0 });
  });
</script>

<ListPane {open}>
  {#snippet list()}
    <div class="grid gap-6">
      <PageHeader
        title="Tags"
        description="The one list of tags, their aliases and colors, and what each is used by."
      />
      <TagList tags={rows} {search} empty={data.tags.length === 0} />
    </div>
  {/snippet}
  {#snippet bar()}
    {@const href = (n: number, size: number) => listHref(pagingSearch({ page: n, size }))}
    <ListBar>
      {#snippet start()}
        <PagerSummary {paging} noun="tags" />
        <PageSize {paging} {href} />
      {/snippet}
      <Pager {paging} {href} />
      {#snippet actions()}
        <Button href={paneHref('+', search)}>+ Add tag</Button>
      {/snippet}
    </ListBar>
  {/snippet}
  {@render children()}
</ListPane>
