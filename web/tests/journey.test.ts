import { expect, test, type Locator, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { e2eFile, mintEnrollmentToken, webOrigin } from './support/environment.ts';
import { addVirtualAuthenticator, type VirtualAuthenticator } from './support/webauthn.ts';

// One browser that goes through the admin area in order: enrolling, signing out and in, and then the passkey page. What
// each step does depends on the passkeys the steps before it left, because the point is the set changing, so these run
// in order and share a database with nothing else (a second enrollment elsewhere would add a passkey to the account).
test.describe.configure({ mode: 'serial' });

let page: Page;
let first: VirtualAuthenticator;

test.beforeAll(async ({ browser }) => {
  page = await (await browser.newContext({ baseURL: webOrigin })).newPage();
  first = await addVirtualAuthenticator(page);
});

test.afterAll(async () => {
  await page.context().close();
});

test('registers a passkey from the link, signs out, and signs back in with it', async () => {
  const database: string = JSON.parse(readFileSync(e2eFile, 'utf8')).database;

  await page.goto(`/admin#token=${mintEnrollmentToken(database)}`);
  await page.getByLabel('What to call it').fill('First key');
  await page.getByRole('button', { name: 'Register passkey' }).click();

  await expect(page.getByText('Signed in as E2E Admin.')).toBeVisible();
  expect(page.url()).not.toContain('token=');
  expect(await first.credentialCount()).toBe(1);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
  await expect(page.getByText('Signed in as E2E Admin.')).toBeVisible();

  await page.getByRole('link', { name: 'Passkeys' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Passkeys' })).toBeVisible();
});
const row = (name: string) => page.getByRole('row').filter({ has: page.getByRole('rowheader', { name, exact: true }) });

test('lists the passkey the account was enrolled with, and will not remove the only one', async () => {
  await expect(row('First key')).toContainText('Security key');
  await expect(row('First key').getByRole('button', { name: 'Remove' })).toBeDisabled();
});

test('refuses a name that is empty or taken before the key makes anything', async () => {
  const before = await first.credentialCount();

  await page.getByRole('button', { name: 'Add a security key' }).click();
  await expect(page.getByText('Give it a name.')).toBeVisible();

  await page.getByLabel('What to call it').fill('First key');
  await page.getByRole('button', { name: 'Add a security key' }).click();
  await expect(page.getByText('Another passkey already has that name.')).toBeVisible();

  expect(await first.credentialCount()).toBe(before);
});

test('adds a second passkey, which the first one has to confirm', async () => {
  // The first key already holds a passkey for this site, so it would refuse to make another (the browser excludes it). It
  // stays silent while the second is made. Then the second stays silent while the first confirms, because an
  // authenticator that answers with "no such passkey" ends the request before the one that has it can answer. The
  // request for the confirmation is held while the test swaps them, so the page cannot ask first.
  const second = await addVirtualAuthenticator(page);
  await first.respond(false);
  const { promise: held, resolve: release } = Promise.withResolvers<void>();
  await page.route('**/auth/passkeys/confirmation/options', async (route) => {
    await held;
    await route.continue();
  });

  await page.getByLabel('What to call it').fill('Second key');
  await page.getByRole('button', { name: 'Add a security key' }).click();
  await expect(page.getByRole('status')).toContainText('confirm with a passkey you already have');
  expect(await second.credentialCount()).toBe(1);
  await second.respond(false);
  await first.respond(true);
  release();

  await expect(row('Second key')).toBeVisible();
  await expect(row('First key')).toBeVisible();
  await page.unroute('**/auth/passkeys/confirmation/options');
});

test('renames a passkey', async () => {
  await row('Second key').getByRole('button', { name: 'Rename' }).click();
  await page.getByLabel('New name for Second key').fill('Spare');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(row('Spare')).toBeVisible();
  await expect(row('Second key')).toHaveCount(0);
});

// What the browser says about the phone flow decides whether its button is there, and Playwright's Chromium cannot say it
// either way, so a test says it for the browser.
async function phoneButtonWhenBrowserSays(hybridTransport: boolean): Promise<Locator> {
  const other = await page.context().newPage();
  await other.addInitScript((supported) => {
    PublicKeyCredential.getClientCapabilities = () => Promise.resolve({ hybridTransport: supported });
  }, hybridTransport);
  await other.goto('/admin/passkeys');
  await expect(other.getByRole('heading', { level: 1, name: 'Passkeys' })).toBeVisible();
  return other.getByRole('button', { name: 'Add a phone' });
}

test('disables the phone button, and says why, when the browser says it cannot make a passkey on a phone', async () => {
  const button = await phoneButtonWhenBrowserSays(false);

  await expect(button).toBeDisabled();
  await expect(button.page().getByText('This browser cannot add a phone.')).toBeVisible();
  await button.page().close();
});

test('leaves the phone button when the browser says it can', async () => {
  const button = await phoneButtonWhenBrowserSays(true);

  await expect(button).toBeEnabled();
  await button.page().close();
});

test('removes a passkey once a passkey that is left has confirmed it', async () => {
  await row('Spare').getByRole('button', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'Remove Spare?' }).click();

  await expect(row('Spare')).toHaveCount(0);
  await expect(row('First key').getByRole('button', { name: 'Remove' })).toBeDisabled();
});
