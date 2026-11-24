<script lang="ts">
  import type { TagResponse } from '#lib/api/schema.ts';
  import Tag from '#lib/components/tag.svelte';
  import * as Table from '#lib/components/ui/table/index.ts';

  import { paneHref } from './routes.ts';

  // The tags as a table: the tag as it looks, its slug, and its aliases. More columns (how many skills, posts, and so on
  // use each tag) will come as those exist. `search` is the list's page, which each row's address carries so closing the
  // editor comes back to it. Used by: the tags page.
  let { tags, search, empty }: { tags: readonly TagResponse[]; search: string; empty: boolean } = $props();
</script>

{#if empty}
  <p class="text-sm text-muted-foreground">There are no tags yet. Add the first one.</p>
{:else}
  <Table.Root>
    <Table.Header>
      <Table.Row>
        <Table.Head>Tag</Table.Head>
        <Table.Head>Slug</Table.Head>
        <Table.Head>Aliases</Table.Head>
      </Table.Row>
    </Table.Header>
    <Table.Body>
      {#each tags as tag (tag.tagId)}
        {@const href = paneHref(tag.tagId, search)}
        <Table.Row data-testid="tag-row">
          <Table.Cell><Tag {tag} {href} /></Table.Cell>
          <Table.Cell class="font-mono text-sm"
            ><a class="underline-offset-4 hover:underline" {href}>{tag.slug}</a></Table.Cell
          >
          <Table.Cell class="text-sm text-muted-foreground">{tag.aliases.join(', ')}</Table.Cell>
        </Table.Row>
      {/each}
    </Table.Body>
  </Table.Root>
{/if}
