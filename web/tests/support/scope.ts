import { type APIRequestContext, type BrowserContext, expect, test as base } from '@playwright/test';
import { SQL } from 'bun';

import { apiUrl, e2e, ownerLogin, webOrigin } from './environment.ts';
import { type Layer, templateName } from './seed.ts';
import { startSession } from './session.ts';

// A database of its own for each test (ADR 0033, a scoped database per test). Import `test` from here, not from
// `@playwright/test`, and the test's `page` runs on a database cloned from a seed layer, so it can change anything and
// can run beside any other test:
//   1. the clone is `CREATE DATABASE seed_test_<id> TEMPLATE <the layer's template>`, straight to the server, which takes
//      about 15 ms (the seed tool costs a process, 0.4 s, for the same statement),
//   2. the browser context adds `X-Test-Scope: <id>` to every request to the web server and the API, which the web
//      server passes on to the calls it makes for a page (`handleFetch`) and the API turns into a connection to that
//      database (`ScopedDatabaseMiddleware`), and
//   3. the database is dropped when the test ends (`WITH (FORCE)`, so a request still running does not stop it).
// A test picks its layer with `test.use({ layer: 'more-tags' })`. The layers are in `seed.ts`, and their templates' names
// were found when the run started (`api.ts`). `test.use({ layer: 'account', signedIn: true })` also signs the layer's
// account in (`session.ts`): a session row in that database and the cookie in the context, so the test can open any page
// of the admin directly. Without `signedIn` the browser is a stranger, as a browser is that has not signed in. A run that is killed leaves its databases behind: `mise seed:clean` drops
// them, and so does the next `mise seed:clean`.

/** The database of one test. */
export type Scope = {
  /** What the header carries: lowercase letters and digits. */
  id: string;
  /** The database, `seed_test_<id>`, named like the seed tool's own. */
  database: string;
  layer: Layer;
};

export const scopeHeader = 'X-Test-Scope';

// The server's own answers: a template another session is connected to for a moment, and one that is gone (the seeds were
// baked again since the run began, and `seed:clean` dropped the old template).
const sourceInUse = '55006';
const noSuchDatabase = '3D000';
const attempts = 50;

type TestFixtures = {
  layer: Layer;
  signedIn: boolean;
  scope: Scope;
  /** The API, for a test to call as the browser could: on the test's database, as its session, from the allowed origin. */
  api: APIRequestContext;
  /** The web server, for a test that wants a page as the server sends it (no scripts run, no styles loaded). */
  web: APIRequestContext;
};
type WorkerFixtures = { owner: SQL; templates: Map<string, string> };

export const test = base.extend<TestFixtures, WorkerFixtures>({
  layer: ['minimal', { option: true }],
  signedIn: [false, { option: true }],

  // One connection to the server per worker, for the creates and the drops.
  owner: [
    // eslint-disable-next-line no-empty-pattern -- Playwright reads the fixtures a function asks for from its first parameter
    async ({}, use) => {
      const owner = new SQL({ ...ownerLogin(), max: 2 });
      await use(owner);
      await owner.close();
    },
    { scope: 'worker' },
  ],

  // The templates by layer, from the run's start. A worker that finds one gone asks again, for itself.
  templates: [
    // eslint-disable-next-line no-empty-pattern -- as above
    async ({}, use) => {
      await use(new Map(Object.entries(e2e().templates)));
    },
    { scope: 'worker' },
  ],

  scope: async ({ owner, templates, layer }, use) => {
    const id = crypto.randomUUID().replaceAll('-', '');
    const database = `seed_test_${id}`;
    await clone(owner, templates, layer, database);
    try {
      await use({ id, database, layer });
    } finally {
      await owner.unsafe(`DROP DATABASE IF EXISTS "${database}" WITH (FORCE)`);
    }
  },

  api: async ({ playwright, context, scope }, use) => {
    const api = await playwright.request.newContext({
      baseURL: apiUrl,
      extraHTTPHeaders: { [scopeHeader]: scope.id, Origin: webOrigin },
      storageState: await context.storageState(),
    });
    await use(api);
    await api.dispose();
  },

  web: async ({ playwright, context, scope }, use) => {
    const web = await playwright.request.newContext({
      baseURL: webOrigin,
      extraHTTPHeaders: { [scopeHeader]: scope.id },
      storageState: await context.storageState(),
    });
    await use(web);
    await web.dispose();
  },

  // Every request the page makes to the web server or the API carries the scope. Added to the request on its way out, so
  // the browser's own checks (a cross-origin call with a header it did not send) never see it.
  context: async ({ context, scope, signedIn }, use) => {
    await sendScope(context, scope.id);
    if (signedIn) await context.addCookies([await startSession(scope.database)]);
    await use(context);
  },
});

export { expect };

/**
 * Makes every request a browser context sends to the web server or the API carry the scope, added on its way out, so the
 * browser's own checks (a cross-origin call with a header it did not send) never see it. The `context` fixture does it for
 * the test's own; a test that opens another browser (`browser.newContext()`) does it for that one. A test that routes a
 * request itself (`page.route`) passes it on with `route.fallback()`: `route.continue()` skips this one, and the request would
 * reach the default database.
 */
export async function sendScope(context: BrowserContext, id: string): Promise<void> {
  for (const origin of [webOrigin, apiUrl]) {
    await context.route(`${origin}/**`, (route) =>
      route.continue({ headers: { ...route.request().headers(), [scopeHeader]: id } }),
    );
  }
}

async function clone(owner: SQL, templates: Map<string, string>, layer: Layer, database: string): Promise<void> {
  let askedAgain = false;
  for (let attempt = 1; ; attempt++) {
    const template = templates.get(layer);
    if (template === undefined) throw new Error(`${layer} is not one of the layers in tests/support/seed.ts.`);
    try {
      await owner.unsafe(`CREATE DATABASE "${database}" TEMPLATE "${template}"`);
      return;
    } catch (error) {
      const state = sqlState(error);
      if (state === sourceInUse && attempt < attempts) {
        await Bun.sleep(100);
      } else if (state === noSuchDatabase && !askedAgain) {
        askedAgain = true;
        templates.set(layer, templateName(layer));
      } else {
        throw error;
      }
    }
  }
}

// The server's own code for what went wrong (`Bun.SQL` puts it in `errno`), or undefined for an error that is not one.
function sqlState(error: unknown): string | undefined {
  return typeof error === 'object' && error !== null && 'errno' in error && typeof error.errno === 'string'
    ? error.errno
    : undefined;
}
