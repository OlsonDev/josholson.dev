import type { DuplicateRequest } from './request.ts';

// The client's own frames (the runtime and the generated functions) say nothing about who asked; the first frame outside
// them does.
const callerOf = (stack: string): string =>
  stack
    .split('\n')
    .filter((line) => !line.includes('/lib/api/'))
    .join('\n');

/**
 * Writes a request that two callers asked for at once to the console, with both call stacks. For development: the
 * request is sent once either way, but two callers asking usually means a component re-runs its `load` or an effect when
 * it should not, and a warning is how that gets found before it is five requests.
 */
export function warnAboutDuplicate(duplicate: DuplicateRequest): void {
  console.warn(
    `${duplicate.operation}: ${duplicate.method} ${duplicate.url} was asked for again while it was in flight, so ` +
      `the two calls share one request.\nFirst asked from:\n${callerOf(duplicate.first)}\n` +
      `Asked again from:\n${callerOf(duplicate.second)}`,
  );
}
