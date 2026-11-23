<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';

  import { colorMode } from '#lib/hooks/color-mode.svelte.ts';

  import { bestText, paintedAs, type PaletteColor, paletteVar } from '../colors.ts';

  // One swatch of the palette: a radio button painted in the stored color as the current mode paints it. Where you are is
  // the ring (the same for the pointer and the keys), and the chosen color is the check in it. The ring is the page's
  // foreground, so it is white in dark mode and black in light mode, with a gap of the popover's own color so it shows
  // against a swatch of its color. It is raised (`z-10`), since a ring reaches past the 2px between swatches and a later
  // neighbor would paint over it. A `tall` swatch takes three of the six rows. Entering a swatch takes the focus to it
  // (`onmouseenter`), and only entering does: a mouse that drifts inside the swatch it rests on does not take the place
  // from the keys. Used by: `SwatchPicker`.
  let {
    stored,
    label,
    tall = false,
    checked,
    ringed,
    tabbable,
    onpick,
    onactivate,
  }: {
    stored: PaletteColor;
    label: string;
    tall?: boolean;
    checked: boolean;
    ringed: boolean;
    tabbable: boolean;
    onpick: () => void;
    onactivate: () => void;
  } = $props();

  const painted = $derived(paintedAs(stored, colorMode.current));
</script>

<button
  type="button"
  role="radio"
  aria-checked={checked}
  aria-label={label}
  tabindex={tabbable ? 0 : -1}
  class="relative flex w-6 items-center justify-center rounded-sm outline-none {tall
    ? 'h-19'
    : 'h-6'} ring-foreground ring-offset-popover {ringed ? 'z-10 ring-2 ring-offset-1' : ''}"
  style:background-color={paletteVar(painted)}
  onclick={onpick}
  onfocus={onactivate}
  onmouseenter={(event) => event.currentTarget.focus({ preventScroll: true })}
>
  {#if checked}
    <CheckIcon class="size-4" style="color: {paletteVar(bestText(painted))}" aria-hidden="true" />
  {/if}
</button>
