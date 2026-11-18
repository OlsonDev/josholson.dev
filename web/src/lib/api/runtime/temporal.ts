/**
 * Makes sure `Temporal` exists. Chrome, Edge, Firefox, Bun, and Node 26 have it; Safari does not yet, so there the
 * polyfill is fetched, as its own chunk, and nowhere else. Await it once at startup (SvelteKit's `init` hook), so every
 * conversion after that is synchronous.
 */
export async function ensureTemporal(): Promise<void> {
  if ('Temporal' in globalThis) return;
  await import('temporal-polyfill/global');
}
