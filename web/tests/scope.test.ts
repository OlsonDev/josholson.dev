import type { APIRequestContext, Page } from '@playwright/test';

import { apiUrl } from './support/environment.ts';
import { expect, scopeHeader, test } from './support/scope.ts';

// The scoped databases themselves (ADR 0033, a scoped database per test): that each test's page runs on a database of
// its own, for the calls the browser makes and for the page the server draws.

// How many tags a response lists: what `GET /tags` answers with is an array.
function countOf(body: unknown): number {
  if (!Array.isArray(body)) throw new Error('GET /tags did not answer with a list.');
  return body.length;
}

async function tagCountFromTheBrowser(page: Page): Promise<number> {
  const body: unknown = await page.evaluate(async (url) => (await fetch(`${url}/tags`)).json(), apiUrl);
  return countOf(body);
}

async function tagCountFromTheApi(request: APIRequestContext, scope?: string): Promise<number> {
  const response = await request.get(
    `${apiUrl}/tags`,
    scope === undefined ? {} : { headers: { [scopeHeader]: scope } },
  );
  const body: unknown = await response.json();
  return countOf(body);
}

test.describe('a scope of the some-tags layer', () => {
  test.use({ layer: 'some-tags' });

  test("has the layer's tags, drawn by the server and the same ones the browser fetches", async ({ page, scope }) => {
    expect(scope.layer).toBe('some-tags');
    await page.goto('/');

    const tagsOnThePage = await page.getByRole('region', { name: 'Tags' }).getByRole('listitem').count();
    expect(tagsOnThePage).toBeGreaterThan(0);
    expect(tagsOnThePage).toBe(await tagCountFromTheBrowser(page));
  });
});

test.describe('a scope of the more-tags layer', () => {
  test.use({ layer: 'more-tags' });

  test("has that layer's tags, drawn by the server and fetched by the browser, and not the default database's", async ({
    page,
    request,
    scope,
  }) => {
    await page.goto('/');

    const tagsOnThePage = await page.getByRole('region', { name: 'Tags' }).getByRole('listitem').count();
    const tagsInTheBrowser = await tagCountFromTheBrowser(page);
    const tagsInTheDefaultDatabase = await tagCountFromTheApi(request);
    const tagsInThisScope = await tagCountFromTheApi(request, scope.id);

    expect(tagsInThisScope).toBeGreaterThan(tagsInTheDefaultDatabase);
    expect(tagsInTheBrowser).toBe(tagsInThisScope);
    expect(tagsOnThePage).toBe(tagsInThisScope);
  });
});

test('a scope that does not exist is refused, and never answered from the default database', async ({ request }) => {
  const response = await request.get(`${apiUrl}/tags`, { headers: { [scopeHeader]: 'nosuchscope' } });

  expect(response.status()).toBe(400);
});

// One after the other on one worker: the second looks for what the first one's scope left behind.
test.describe('the database of a test', () => {
  test.describe.configure({ mode: 'serial' });
  let database = '';

  const exists = async (owner: { unsafe: (sql: string, values: string[]) => Promise<unknown[]> }): Promise<boolean> =>
    (await owner.unsafe('SELECT 1 FROM pg_database WHERE datname = $1', [database])).length === 1;

  test('is there while the test runs', async ({ page, scope, owner }) => {
    database = scope.database;
    await page.goto('/');

    expect(await exists(owner)).toBe(true);
  });

  test('is gone when the test has ended', async ({ owner }) => {
    expect(database).not.toBe('');

    expect(await exists(owner)).toBe(false);
  });
});
