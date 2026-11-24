import { defineConfig } from '@playwright/test';

// Two servers: the API on a database of its own (tests/support/api.ts), and a production build of the web app pointed at
// it. To watch a run: `mise web:test:headed` (a visible browser), or `mise web:test:ui` (Playwright's UI, with a timeline
// and a snapshot of every step); `PW_SLOW_MO=500` slows each action by that many milliseconds.
const apiUrl = 'http://localhost:5100';

export default defineConfig({
  testDir: 'tests',
  // Still one worker: a test that imports `test` from tests/support/scope.ts has a database of its own, but the other files
  // share the API's default one, and the journey's sign-in depends on the enrollment before it. As each file moves to its
  // own, this goes (ADR 0033, a scoped database per test).
  workers: 1,
  fullyParallel: false,
  webServer: [
    {
      command: 'bun tests/support/api.ts',
      url: `${apiUrl}/tags`,
      timeout: 240_000,
      reuseExistingServer: false,
      gracefulShutdown: { signal: 'SIGTERM', timeout: 60_000 },
    },
    {
      command: 'bun x vite build && bun x vite preview --port 4173 --strictPort',
      port: 4173,
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
      // TEST_SCOPES: a page request's `X-Test-Scope` is passed on to the calls the server makes to the API (hooks.server.ts).
      env: { API_URL: apiUrl, PUBLIC_API_URL: apiUrl, TEST_SCOPES: 'true' },
    },
  ],
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'retain-on-failure',
    launchOptions: { slowMo: Number(process.env.PW_SLOW_MO ?? 0) },
  },
});
