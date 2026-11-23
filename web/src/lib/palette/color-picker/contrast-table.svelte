<script lang="ts">
  import MoonIcon from '@lucide/svelte/icons/moon';
  import SunIcon from '@lucide/svelte/icons/sun';
  import type { Snippet } from 'svelte';

  import {
    aaaVerdict,
    aaVerdict,
    contrastRatio,
    formatRatio,
    type Mode,
    type PaletteColor,
    paletteVar,
    textCandidates,
  } from '../colors.ts';
  import type { PreviewColors } from './color-picker.ts';
  import VerdictMark from './verdict-mark.svelte';

  // How text reads on a background in one mode: the text colors worth trying, each with its contrast ratio and whether it
  // meets AA and AAA. The header names the mode and the background. `preview` draws the sample in each row (see
  // `ColorPicker`); without one it is "Aa". Used by: `ContrastPanel`.
  let { mode, background, preview }: { mode: Mode; background: PaletteColor; preview?: Snippet<[PreviewColors]> } =
    $props();

  const rows = $derived(
    textCandidates(background, mode).map((text) => ({ text, ratio: contrastRatio(background, text) })),
  );
</script>

<!-- Static widths for what changes as the pointer moves (a color name, a ratio, a mark), one line to a row, and the
     preview column as wide as its widest text: the mode's name in the header or the preview text, which are the same
     for every swatch. So hovering moves nothing, and the table is only as wide as it needs to be. The header names the
     mode and the color under the pointer, and the rows are the text to try on it. -->
<table
  class="w-max self-start text-xs [&_td]:py-0.5 [&_td]:pr-2 [&_th]:py-1 [&_th]:pr-2 [&_td:last-child]:pr-0 [&_th:last-child]:pr-0"
>
  <colgroup>
    <col />
    <col class="w-[6.5rem]" />
    <col class="w-14" />
    <col class="w-8" />
    <col class="w-9" />
  </colgroup>
  <thead>
    <tr class="text-left font-medium text-muted-foreground">
      <th class="align-middle text-sm font-semibold text-foreground">
        <span class="flex items-center gap-1.5 whitespace-nowrap">
          {#if mode === 'light'}
            <SunIcon class="size-4" aria-hidden="true" />
            Light mode
          {:else}
            <MoonIcon class="size-4" aria-hidden="true" />
            Dark mode
          {/if}
        </span>
      </th>
      <th class="align-middle whitespace-nowrap text-foreground" data-testid="background-{mode}">
        <code class="font-mono font-normal">{background}</code>
      </th>
      <th class="text-right align-middle">Ratio</th>
      <th class="align-middle">AA</th>
      <th class="align-middle">AAA</th>
    </tr>
  </thead>
  <tbody>
    {#each rows as row (row.text)}
      {@const colors = { background: paletteVar(background), color: paletteVar(row.text) }}
      <tr>
        <td>
          {#if preview !== undefined}
            {@render preview(colors)}
          {:else}
            <span
              class="inline-block rounded-full px-2.5 py-0.5 align-middle text-xs font-medium"
              style:background-color={colors.background}
              style:color={colors.color}
            >
              Aa
            </span>
          {/if}
        </td>
        <td class="whitespace-nowrap"><code class="font-mono">{row.text}</code></td>
        <td class="text-right font-mono tabular-nums">{formatRatio(row.ratio)}</td>
        <td><span class="flex h-5 items-center"><VerdictMark verdict={aaVerdict(row.ratio)} srLabel /></span></td>
        <td><span class="flex h-5 items-center"><VerdictMark verdict={aaaVerdict(row.ratio)} srLabel /></span></td>
      </tr>
    {/each}
  </tbody>
</table>
