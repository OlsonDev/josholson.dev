import { availableParallelism } from 'node:os';

import { defineConfig } from '@playwright/test';

// Two servers: the API on a database of its own (tests/support/api.ts), and a production build of the web app pointed at
// it. To watch a run: `mise web:test:headed` (a visible browser), or `mise web:test:ui` (Playwright's UI, with a timeline
// and a snapshot of every step); `PW_SLOW_MO=500` slows each action by that many milliseconds.
const apiUrl = 'http://localhost:5100';

export default defineConfig({
  testDir: 'tests',
  // Every test has a database of its own (tests/support/scope.ts, ADR 0033), so nothing depends on the order or on another
  // test, and they all run side by side: `PW_WORKERS=1` for a run in order (to read one, say). Half the cores, and no more
  // than eight: a run of 50 tests took 46 s on one worker, 22 s on four, 19 s on six, 18.8 s on eight, and 18.3 s on sixteen, the
  // rest of it the servers starting and the longest test, and each worker is a browser and its connections.
  workers: Number(process.env['PW_WORKERS'] ?? Math.min(8, Math.max(1, Math.floor(availableParallelism() / 2)))),
  fullyParallel: true,
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
