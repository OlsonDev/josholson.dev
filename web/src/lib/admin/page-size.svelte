<script lang="ts">
  import { goto } from '$app/navigation';

  import { pageSizes, type Paging } from './list-state.ts';

  // The page sizes a list offers. Choosing another keeps the first row that is showing on screen. Used by: tags.
  let { paging, href }: { paging: Paging; href: (page: number, size: number) => string } = $props();
</script>

<label class="flex items-center gap-2 text-sm whitespace-nowrap text-muted-foreground">
  Per page
  <select
    class="h-8 rounded-md border border-input bg-transparent px-2 text-sm text-foreground shadow-xs outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
    value={paging.size}
    onchange={(event) => {
      const size = Number(event.currentTarget.value);
      void goto(href(Math.floor(paging.start / size) + 1, size));
    }}
  >
    {#each pageSizes as size (size)}
      <option value={size}>{size}</option>
    {/each}
  </select>
</label>
