<script lang="ts">
  import type { Snippet } from 'svelte';

  // The bar along the bottom of a list (`ListPane`'s `bar`), in three places: what says where you are in the list on the
  // left (`start`: the rows showing, the page size), what moves through it in the middle (the page links), and what acts on
  // the list at the right edge (`actions`: "+ Add tag"). The middle is the middle of the bar whatever the two sides hold,
  // so a change at one side moves nothing at the other. On a narrow screen they wrap. Used by: tags.
  let { start, children, actions }: { start?: Snippet; children: Snippet; actions?: Snippet } = $props();
</script>

<div
  class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t bg-background px-4 py-3 md:grid md:grid-cols-[1fr_auto_1fr] md:px-6"
>
  <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
    {#if start}{@render start()}{/if}
  </div>
  <div class="flex items-center justify-center">{@render children()}</div>
  <div class="flex items-center justify-end gap-2">
    {#if actions}{@render actions()}{/if}
  </div>
</div>
