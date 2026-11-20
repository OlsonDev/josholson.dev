<script lang="ts">
  import Tag from '#lib/components/tag.svelte';

  import type { PageProps } from './$types';

  let { data }: PageProps = $props();
</script>

<svelte:head>
  <title>Josh Olson</title>
</svelte:head>

<h1 class="text-3xl font-bold tracking-tight">Josh Olson</h1>

<section aria-labelledby="tags-heading" class="mt-8">
  <h2 id="tags-heading" class="text-xl font-semibold">Tags</h2>
  {#if data.tags === null}
    <p class="mt-2 text-muted-foreground">Tags are unavailable right now.</p>
  {:else if data.tags.length === 0}
    <p class="mt-2 text-muted-foreground">No tags yet.</p>
  {:else}
    <ul class="mt-2 flex flex-wrap gap-2">
      {#each data.tags as tag (tag.tagId)}
        <li><Tag {tag} /></li>
      {/each}
    </ul>
  {/if}
</section>
