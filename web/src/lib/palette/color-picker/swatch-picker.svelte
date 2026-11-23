<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';

  import { Button } from '#lib/components/ui/button/index.ts';
  import { colorMode } from '#lib/hooks/color-mode.svelte.ts';

  import {
    colorName,
    displayWeight,
    families,
    mirror,
    offeredWeights,
    type PaletteColor,
    parseColor,
    storedWeight,
  } from '../colors.ts';
  import ColorSwatch from './color-swatch.svelte';
  import NoColorSwatch from './no-color-swatch.svelte';
  import { type EnteredFrom, neighbor, NO_COLOR } from './palette-keys.ts';

  // The swatches to pick from, and "No color" when the color is optional (`allowNone`), which is above them and in the
  // keyboard's reach. What a swatch stores is its light-mode name, and the grid shows the weights from the end of the scale
  // the page is in (50 to 500 in light mode, 950 to 500 in dark), so a choice means the same in either. `active` is where
  // you are in the palette: the swatch with the focus, which is the one the last key press or pointer move landed on. The
  // pointer and the keys share this one place (moving the pointer over a swatch gives it the focus, and keys go on from
  // it), so there is a single ring, and the 2px between swatches belong to the swatch the pointer came from, so crossing a
  // gap is not a moment with nothing active. Null on "No color", and when focus is outside the palette. Leaving the
  // palette changes nothing else: the place stays where it was last put, and only opening the picker starts it over
  // (`focus`). Used by: `ColorPicker`.
  let {
    value,
    allowNone = false,
    active = $bindable(null),
    onpick,
  }: {
    value: PaletteColor | null;
    allowNone?: boolean;
    active?: PaletteColor | null;
    onpick: (stored: PaletteColor | null) => void;
  } = $props();

  let grid = $state<HTMLElement | null>(null);
  let noColorButton = $state<HTMLElement | null>(null);

  const mode = $derived(colorMode.current);
  const weightsShown = $derived(offeredWeights[mode]);
  // Stored as light mode paints them, white on top and black below. Dark mode paints the mirror, so the top one is black
  // there: in both modes the swatch at the top is the one that sits at the top of the page's own scale.
  const extremes = ['white', 'black'];

  // The stored color a swatch picks.
  const picks = (family: string, shown: number): PaletteColor => colorName(family, storedWeight(shown, mode));

  // Arrow keys move through the swatches like a grid, the way a group of radio buttons is expected to (`palette-keys.ts`). The
  // swatches are in the page in reading order, the families' rows and then the two tall ones.
  let enteredFrom: EnteredFrom = null;
  // "No color" (when there is one) is above the palette, so up from the top row goes to it, and down from it goes back to
  // where up came from. With nowhere to go back to (the picker opened on it), down goes to the top of the tall column.
  let cameFrom: number | null = null;
  const shape = $derived({ columns: families.length, rows: weightsShown.length, extremes: extremes.length });

  // Down from "No color" goes back to the swatch up came from, or to the top of the tall column when it did not.
  function onNoColorKeydown(event: KeyboardEvent): void {
    if (event.key !== 'ArrowDown' || grid === null) return;
    const swatches = [...grid.querySelectorAll<HTMLElement>('[role="radio"]')];
    const target = swatches[cameFrom ?? families.length * weightsShown.length];
    if (target === undefined) return;
    event.preventDefault();
    cameFrom = null;
    target.focus();
  }

  function onkeydown(event: KeyboardEvent): void {
    if (grid === null) return;
    const swatches = [...grid.querySelectorAll<HTMLElement>('[role="radio"]')];
    const at = swatches.indexOf(document.activeElement as HTMLElement);
    // With nothing chosen and no "No color", the picker opens with no swatch active, and a key goes to the first one.
    if (document.activeElement === grid && event.key.startsWith('Arrow')) {
      event.preventDefault();
      swatches[0]?.focus();
      return;
    }
    if (at < 0) return;
    const next = neighbor(at, event.key, shape, enteredFrom, allowNone);
    if (next === NO_COLOR) {
      event.preventDefault();
      cameFrom = at;
      enteredFrom = null;
      noColorButton?.focus();
      return;
    }
    const target = next === null ? undefined : swatches[next];
    if (target === undefined || next === null) return;
    event.preventDefault();
    cameFrom = null;

    // Remember the row a move right into the tall column started from, for the move back. Any other key forgets it.
    const cells = families.length * weightsShown.length;
    enteredFrom =
      event.key === 'ArrowRight' && at < cells && next >= cells
        ? { extreme: next - cells, row: Math.floor(at / families.length) }
        : null;
    target.focus();
  }

  // The grid is one tab stop, on the chosen swatch (or the first one). A chosen color the grid does not offer (a weight of
  // 600 or more in light mode) leaves it the first swatch.
  const firstSwatch = $derived(colorName(families[0], storedWeight(weightsShown[0] ?? 50, mode)));
  const isOffered = (stored: PaletteColor): boolean => {
    if (extremes.includes(stored)) return true;
    const parsed = parseColor(stored);
    return parsed !== null && weightsShown.includes(displayWeight(parsed.weight, mode));
  };
  const tabStop = $derived(value !== null && isOffered(value) ? value : firstSwatch);

  /**
   * Puts the focus where the choice is: on the chosen swatch, or on "No color" when that is the choice, so a key press
   * starts from it and a second Space picks what was already picked.
   */
  export function focus(): void {
    // Nothing chosen and no "No color" to be on: the palette itself, with no swatch active until a key or the pointer picks one.
    const chosen =
      value !== null
        ? grid?.querySelector<HTMLElement>('[role="radio"][tabindex="0"]')
        : allowNone
          ? noColorButton
          : grid;
    if (chosen === grid) active = null;
    chosen?.focus({ preventScroll: true });
  }
</script>

<!-- Nothing in the popover shrinks to fit a short window: it scrolls instead. -->
<div class="flex shrink-0 items-center justify-between gap-4">
  <p class="text-xs text-muted-foreground">
    Weights for {mode} mode. The color is kept as its {mode}-mode one and mirrored in the other.
  </p>
  {#if allowNone}
    <Button
      bind:ref={noColorButton}
      variant="ghost"
      size="sm"
      class="gap-2"
      onclick={() => onpick(null)}
      onkeydown={onNoColorKeydown}
      onfocus={() => (active = null)}
    >
      <NoColorSwatch />
      No color
      {#if value === null}
        <CheckIcon class="size-4" aria-hidden="true" />
      {/if}
    </Button>
  {/if}
</div>
<div
  bind:this={grid}
  role="radiogroup"
  aria-label="Color"
  tabindex="-1"
  class="flex shrink-0 gap-0.5"
  {onkeydown}
  onpointerdown={() => (enteredFrom = null)}
  onfocusout={(event) => {
    if (!(event.relatedTarget instanceof Node && grid?.contains(event.relatedTarget))) active = null;
  }}
>
  <div class="grid gap-0.5" style:grid-template-columns="repeat({families.length}, 1.5rem)">
    {#each weightsShown as shown (shown)}
      {#each families as family (family)}
        {@const stored = picks(family, shown)}
        <ColorSwatch
          {stored}
          label={colorName(family, shown)}
          checked={value === stored}
          ringed={active === stored}
          tabbable={stored === tabStop}
          onpick={() => onpick(stored)}
          onactivate={() => (active = stored)}
        />
      {/each}
    {/each}
  </div>
  <!-- Each takes three of the six rows. -->
  <div class="flex flex-col gap-0.5">
    {#each extremes as extreme (extreme)}
      <ColorSwatch
        stored={extreme}
        label={colorMode.current === 'light' ? extreme : mirror(extreme)}
        tall
        checked={value === extreme}
        ringed={active === extreme}
        tabbable={extreme === tabStop}
        onpick={() => onpick(extreme)}
        onactivate={() => (active = extreme)}
      />
    {/each}
  </div>
</div>
