<script lang="ts">
  import type { Snippet } from 'svelte';

  // The top of an admin page: its one `<h1>`, what it is for, and the buttons that act on the page as a whole.
  let { title, description, actions }: { title: string; description?: string; actions?: Snippet } = $props();
</script>

<svelte:head>
  <title>{title} · Admin</title>
  <meta name="robots" content="noindex" />
</svelte:head>

<div class="flex items-start justify-between gap-4">
  <div class="space-y-1">
    <h1 class="text-2xl font-semibold tracking-tight">{title}</h1>
    {#if description}
      <p class="text-sm text-muted-foreground">{description}</p>
    {/if}
  </div>
  {#if actions}
    <div class="flex shrink-0 gap-2">{@render actions()}</div>
  {/if}
</div>
