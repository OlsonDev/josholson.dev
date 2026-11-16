<script lang="ts">
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();
</script>

<svelte:head>
  <title>Josh Olson</title>
</svelte:head>

<h1>Josh Olson</h1>

<section aria-labelledby="tags-heading">
  <h2 id="tags-heading">Tags</h2>
  {#if data.tags === null}
    <p>Tags are unavailable right now.</p>
  {:else if data.tags.length === 0}
    <p>No tags yet.</p>
  {:else}
    <ul class="tags">
      {#each data.tags as tag (tag.tagId)}
        <li style:--tag-color={tag.color}>{tag.name}</li>
      {/each}
    </ul>
  {/if}
</section>

<style>
  .tags {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
    padding: 0;
    list-style: none;
  }

  .tags li {
    padding: var(--space-1) var(--space-3);
    border: 1px solid var(--tag-color, var(--border));
    border-radius: 999px;
  }
</style>
