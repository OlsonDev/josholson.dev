<script lang="ts">
  import { type Snippet, tick } from 'svelte';
  import { cubicOut } from 'svelte/easing';
  import { MediaQuery } from 'svelte/reactivity';
  import { fade, fly, slide } from 'svelte/transition';

  import { providePane } from './pane.svelte.ts';

  // A list with its edit form beside it, for every list and edit page of the admin. The list is always here. When `open`
  // is true (the URL is a pane's, such as /admin/tags/5), the list slides left, out of sight at the edge of the content
  // area, which is under the sidebar, and the pane slides in from the right. Closing is the same in reverse, and since the
  // URL decides, back, Cancel, and a confirmed discard all play it.
  //
  // Along the bottom is one bar at a time: the list's own (`bar`, such as the pager) while the list is showing, and the
  // pane's (`PaneBar`) while a pane is. A bar is a row under the scrolling part, not a layer over it, so a scrollbar ends
  // where the bar begins. The bars grow and shrink in height, which is what makes one rise from the bottom as the other
  // goes. Used by: tags.
  let { open, list, bar, children }: { open: boolean; list: Snippet; bar?: Snippet; children: Snippet } = $props();

  const pane = providePane();
  const reducedMotion = new MediaQuery('prefers-reduced-motion: reduce');

  // Under reduced motion the pane and the bars fade instead of moving.
  function slideIn(node: Element) {
    return reducedMotion.current
      ? fade(node, { duration: 150 })
      : fly(node, { x: '100%', opacity: 1, duration: 300, easing: cubicOut });
  }

  function rise(node: Element) {
    return reducedMotion.current
      ? fade(node, { duration: 150 })
      : slide(node, { axis: 'y', duration: 300, easing: cubicOut });
  }

  let paneElement = $state<HTMLElement | null>(null);
  // The last thing focused in the list, which is what opened the pane (the list goes inert as it opens, and takes focus with it).
  let opener: HTMLElement | null = null;

  // Focus goes to the pane's heading when it opens (so a screen reader starts at the top of it), and back to what opened
  // it, such as the row that was clicked, when it closes.
  let wasOpen = false;
  $effect(() => {
    if (open === wasOpen) return;
    wasOpen = open;
    if (open) {
      void tick().then(() =>
        paneElement?.querySelector<HTMLElement>('[data-pane-heading]')?.focus({ preventScroll: true }),
      );
    } else {
      void tick().then(() => {
        if (opener?.isConnected === true) opener.focus({ preventScroll: true });
      });
    }
  });
</script>

<!-- The negative margins undo the page's padding, so the list is clipped at the edge of the content area. Each scrolling
     part keeps room for its scrollbar on both sides (`scrollbar-gutter`), so content does not move when one appears. -->
<div class="-m-4 flex h-[calc(100svh-3.5rem)] flex-col overflow-hidden md:-m-6">
  <div class="relative min-h-0 flex-1 overflow-hidden">
    <div
      data-list-scroll
      class="absolute inset-0 overflow-y-auto p-4 [scrollbar-gutter:stable_both-edges] transition-transform duration-300 ease-out motion-reduce:transition-opacity md:p-6 {open
        ? '-translate-x-full motion-reduce:opacity-0'
        : ''}"
      inert={open}
      aria-hidden={open}
      onfocusin={(event) => {
        if (event.target instanceof HTMLElement) opener = event.target;
      }}
    >
      {@render list()}
    </div>
    {#if open}
      <div
        bind:this={paneElement}
        data-pane-scroll
        class="absolute inset-0 overflow-y-auto bg-background [scrollbar-gutter:stable_both-edges]"
        transition:slideIn
      >
        {@render children()}
      </div>
    {/if}
  </div>
  {#if open && pane.bar !== null}
    <div class="shrink-0" data-bar="pane" transition:rise>
      {@render pane.bar()}
    </div>
  {:else if !open && bar}
    <div class="shrink-0" data-bar="list" transition:rise>
      {@render bar()}
    </div>
  {/if}
</div>
