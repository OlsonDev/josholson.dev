<script lang="ts">
  import type { Snippet } from 'svelte';

  import { mirror, type PaletteColor } from '../colors.ts';
  import type { PreviewColors } from './color-picker.ts';
  import ContrastTable from './contrast-table.svelte';

  // The panel under the grid: how text reads on `color` in light mode and in dark mode (the stored color and its mirror),
  // or a hint when there is no color. Used by: `ColorPicker`.
  let { color, preview }: { color: PaletteColor | null; preview?: Snippet<[PreviewColors]> } = $props();
</script>

<!-- A fixed height, that of the tallest table (a family has five text colors, black and white one), so the panel does
     not grow and shrink as the pointer moves, and no taller, so there is no slack under it. -->
<div class="grid h-37 shrink-0 gap-4 overflow-hidden sm:grid-cols-2" aria-live="polite" data-testid="contrast-panel">
  {#if color === null}
    <p class="text-sm text-muted-foreground sm:col-span-2">Hover or focus a color to see how text reads on it.</p>
  {:else}
    <ContrastTable mode="light" background={color} {preview} />
    <ContrastTable mode="dark" background={mirror(color)} {preview} />
  {/if}
</div>
