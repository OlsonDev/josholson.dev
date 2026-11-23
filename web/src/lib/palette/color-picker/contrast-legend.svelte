<script lang="ts">
  import { AA, AAA } from '../colors.ts';
  import VerdictMark from './verdict-mark.svelte';

  // What AA and AAA ask for, with the decimals lined up, and what the three marks mean. Used by: `ColorPicker`.
</script>

<div class="flex shrink-0 justify-between gap-6 text-xs" data-testid="contrast-legend">
  <dl class="grid grid-cols-[auto_auto] gap-x-3 gap-y-0.5 font-mono">
    <dt class="text-muted-foreground">AA</dt>
    <dd>≥ {AA.toFixed(1)}</dd>
    <dt class="text-muted-foreground">AAA</dt>
    <dd>≥ {AAA.toFixed(1)}</dd>
  </dl>
  <ul class="grid gap-0.5 text-muted-foreground">
    <li class="flex items-center gap-1.5"><VerdictMark verdict="pass" /> Passes</li>
    <li class="flex items-center gap-1.5"><VerdictMark verdict="barely" /> Passes by ≤ 10%</li>
    <li class="flex items-center gap-1.5"><VerdictMark verdict="fail" /> Fails</li>
  </ul>
</div>
