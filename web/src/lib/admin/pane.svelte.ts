import { getContext, setContext, type Snippet } from 'svelte';

const key = Symbol('list-pane');

/** What a pane (the edit form beside a list) can ask of the list page around it. */
export class PaneContext {
  /** The action bar the pane wants anchored to the bottom, drawn by the list page so it can come in from below. */
  bar = $state.raw<Snippet | null>(null);
}

/** Called by the list page (`ListPane`) for the panes inside it. */
export function providePane(): PaneContext {
  const context = new PaneContext();
  setContext(key, context);
  return context;
}

/** Called by a pane (`PaneBar` does it). */
export function usePane(): PaneContext {
  const context = getContext<PaneContext | undefined>(key);
  if (context === undefined) throw new Error('A pane belongs inside a ListPane.');
  return context;
}
