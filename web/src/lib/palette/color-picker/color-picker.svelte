<script lang="ts">
  import type { Snippet } from 'svelte';

  import { Button } from '#lib/components/ui/button/index.ts';
  import * as Popover from '#lib/components/ui/popover/index.ts';
  import { colorMode } from '#lib/hooks/color-mode.svelte.ts';

  import { paintedAs, type PaletteColor, paletteVar } from '../colors.ts';
  import type { PreviewColors } from './color-picker.ts';
  import ContrastLegend from './contrast-legend.svelte';
  import ContrastPanel from './contrast-panel.svelte';
  import NoColorSwatch from './no-color-swatch.svelte';
  import SectionHeading from './section-heading.svelte';
  import SwatchPicker from './swatch-picker.svelte';

  // Picks a color from the Tailwind palette. What is stored is the color of a background in light mode, and dark mode
  // paints the mirrored weight (`emerald-200` becomes `emerald-800`). Hovering or focusing a swatch shows how text would
  // read on it in both modes: the contrast ratio and whether it meets WCAG 2 AA and AAA. `preview` draws the sample in
  // each row from the row's colors (CSS values for a background and a text), so it reads like the real thing; without one,
  // the sample is "Aa". Its width must not depend on the colors it is given, or the table moves as the pointer does. The
  // parts are beside it: `SwatchPicker` (the grid and the keys), `ContrastPanel`, `ContrastLegend`. Used by: the tag editor.
  //
  // Null is "nothing chosen" (`placeholder`, "None selected"), unless the color is optional (`allowNone`): then null is the
  // choice "No color", which is in the palette like any swatch (above it, and in the keyboard's reach) and has a check
  // when it is the choice. A required color has no such option, and null just means the person has not chosen yet.
  let {
    value = $bindable(),
    preview,
    allowNone = false,
    placeholder = 'None selected',
    id,
    describedBy,
    invalid = false,
    onchange,
  }: {
    value: PaletteColor | null;
    preview?: Snippet<[PreviewColors]>;
    allowNone?: boolean;
    placeholder?: string;
    id?: string;
    describedBy?: string;
    invalid?: boolean;
    onchange?: () => void;
  } = $props();

  let open = $state(false);
  // Where you are in the palette (`SwatchPicker` sets it), which the panel under the grid is about, and otherwise the
  // chosen color.
  let active = $state<PaletteColor | null>(null);
  let swatches = $state<ReturnType<typeof SwatchPicker> | null>(null);

  const inspected = $derived(active ?? value);

  function pick(stored: PaletteColor | null): void {
    value = stored;
    open = false;
    onchange?.();
  }

  // Opening the picker puts the focus where the choice is.
  function focusStart(event: Event): void {
    event.preventDefault();
    swatches?.focus();
  }
</script>

<Popover.Root bind:open>
  <Popover.Trigger>
    {#snippet child({ props })}
      <Button
        {...props}
        {id}
        variant="outline"
        class="w-fit justify-start gap-2"
        aria-describedby={describedBy}
        aria-invalid={invalid}
      >
        {#if value === null && allowNone}
          <NoColorSwatch />
          No color
        {:else if value === null}
          <span class="text-foreground/60">{placeholder}</span>
        {:else}
          <span
            class="size-4 rounded-sm border"
            style:background-color={paletteVar(paintedAs(value, colorMode.current))}
          ></span>
          <code class="font-mono text-sm">{value}</code>
        {/if}
      </Button>
    {/snippet}
  </Popover.Trigger>
  <!-- Every part is the same distance from the next, and from the edge: the popover's padding and its gap are one size. -->
  <Popover.Content
    class="max-h-(--bits-popover-content-available-height) w-auto max-w-[calc(100vw-1rem)] gap-4 overflow-auto"
    align="start"
    onOpenAutoFocus={focusStart}
  >
    <SwatchPicker bind:this={swatches} {value} {allowNone} bind:active onpick={pick} />
    <SectionHeading label="Accessibility" />
    <ContrastPanel color={inspected} {preview} />
    <SectionHeading label="Legend" />
    <ContrastLegend />
  </Popover.Content>
</Popover.Root>
