import { defineConfig } from '@playwright/test';

// Two servers: the API on a database of its own (tests/support/api.ts), and a production build of the web app pointed at
// it. To watch a run: `mise web:test:headed` (a visible browser), or `mise web:test:ui` (Playwright's UI, with a timeline
// and a snapshot of every step); `PW_SLOW_MO=500` slows each action by that many milliseconds.
const apiUrl = 'http://localhost:5100';

export default defineConfig({
  testDir: 'tests',
  // One database, and a sign-in that depends on the enrollment before it.
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
      env: { API_URL: apiUrl, PUBLIC_API_URL: apiUrl },
    },
  ],
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'retain-on-failure',
    launchOptions: { slowMo: Number(process.env.PW_SLOW_MO ?? 0) },
  },
});
