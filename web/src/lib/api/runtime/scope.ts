/** The header a test sends to choose the database a request runs on (ADR 0033, a scoped database per test). */
export const scopeHeader = 'X-Test-Scope';

/**
 * Passes the test scope of the request the web server is serving on to a call it makes to the API, so a page rendered on
 * the server reads the same database the browser's own calls do. It only does so when `enabled` (the web server was
 * started for tests: `TEST_SCOPES`) and the call is to the API's own origin, never to anything else. A request with no
 * scope is left alone, and so is one that already has its own. The API refuses a scope with no database, and a build
 * without its test code ignores the header, so a value that reaches it by mistake does no harm.
 */
export function forwardScope(call: Request, incoming: Headers, apiUrl: string, enabled: boolean): void {
  if (!enabled) return;
  const scope = incoming.get(scopeHeader);
  if (scope === null || call.headers.has(scopeHeader)) return;
  if (new URL(call.url).origin !== new URL(apiUrl).origin) return;
  call.headers.set(scopeHeader, scope);
}
