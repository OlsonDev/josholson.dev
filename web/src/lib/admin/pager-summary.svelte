<script lang="ts">
  import type { Paging } from './list-state.ts';

  // "21–22 of 22 tags": which rows are showing. The range is as wide as the widest it can be (the digits of the total, twice,
  // and the dash, in tabular figures), and right-aligned, so "of 22 tags" does not move as the range changes. Used by: tags.
  let { paging, noun = 'rows' }: { paging: Paging; noun?: string } = $props();

  const first = $derived(paging.total === 0 ? 0 : paging.start + 1);
  const width = $derived(paging.total.toString().length * 2 + 1);
</script>

<p class="text-sm whitespace-nowrap text-muted-foreground tabular-nums" data-testid="paging-summary">
  <span class="inline-block text-right" style="min-width: {width}ch">{first}–{paging.end}</span>
  of {paging.total}
  {noun}
</p>
