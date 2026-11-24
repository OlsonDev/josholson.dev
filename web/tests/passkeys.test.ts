import type { Locator, Page } from '@playwright/test';

import { addPasskeyRow } from './support/passkeys.ts';
import { expect, test } from './support/scope.ts';
import { addVirtualAuthenticator } from './support/webauthn.ts';

// The passkey page of the account (the `account` layer, signed in), with the authenticator that holds its one passkey, the
// way the key the owner enrolled is on the key they hold. Each test has a database of its own (ADR 0033, a scoped
// database per test): one that needs a second passkey in the list is given its row, and none depends on another.
test.use({ layer: 'account', signedIn: true });

// The page, and the authenticator that holds the account's passkey.
async function openPasskeys(page: Page) {
  const first = await addVirtualAuthenticator(page, { accountPasskey: true });
  await page.goto('/admin/passkeys');
  await expect(page.getByRole('heading', { level: 1, name: 'Passkeys' })).toBeVisible();
  return first;
}

const row = (page: Page, name: string) =>
  page.getByRole('row').filter({ has: page.getByRole('rowheader', { name, exact: true }) });

test('lists the passkey the account was enrolled with, and will not remove the only one', async ({ page }) => {
  await openPasskeys(page);

  await expect(row(page, 'First key')).toContainText('Security key');
  await expect(row(page, 'First key').getByRole('button', { name: 'Remove' })).toBeDisabled();
});

test('refuses a name that is empty or taken before the key makes anything', async ({ page }) => {
  const first = await openPasskeys(page);
  const before = await first.credentialCount();

  await page.getByRole('button', { name: 'Add a security key' }).click();
  await expect(page.getByText('Give it a name.')).toBeVisible();

  await page.getByLabel('What to call it').fill('First key');
  await page.getByRole('button', { name: 'Add a security key' }).click();
  await expect(page.getByText('Another passkey already has that name.')).toBeVisible();

  expect(await first.credentialCount()).toBe(before);
});

test('adds a second passkey, which the first one has to confirm', async ({ page }) => {
  const first = await openPasskeys(page);
  // The first key already holds a passkey for this site, so it would refuse to make another (the browser excludes it). It
  // stays silent while the second is made. Then the second stays silent while the first confirms, because an
  // authenticator that answers with "no such passkey" ends the request before the one that has it can answer. The
  // request for the confirmation is held while the test swaps them, so the page cannot ask first.
  const second = await addVirtualAuthenticator(page);
  await first.respond(false);
  const { promise: held, resolve: release } = Promise.withResolvers<void>();
  await page.route('**/auth/passkeys/confirmation/options', async (route) => {
    await held;
    // Not `continue`: that would skip the context's route, which adds the scope, and the API would look in the wrong database.
    await route.fallback();
  });

  await page.getByLabel('What to call it').fill('Second key');
  await page.getByRole('button', { name: 'Add a security key' }).click();
  await expect(page.getByRole('status')).toContainText('confirm with a passkey you already have');
  expect(await second.credentialCount()).toBe(1);
  expect(await second.userNames()).toEqual(['E2E Admin (Second key)']);
  await second.respond(false);
  await first.respond(true);
  release();

  await expect(row(page, 'Second key')).toBeVisible();
  await expect(row(page, 'First key')).toBeVisible();
  await page.unroute('**/auth/passkeys/confirmation/options');
});

test('renames a passkey', async ({ page, scope }) => {
  await addPasskeyRow(scope.database, 'Second key');
  await openPasskeys(page);

  await row(page, 'Second key').getByRole('button', { name: 'Rename' }).click();
  await page.getByLabel('New name for Second key').fill('Spare');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(row(page, 'Spare')).toBeVisible();
  await expect(row(page, 'Second key')).toHaveCount(0);
});

// What the browser says about the phone flow decides whether its button is there, and Playwright's Chromium cannot say it
// either way, so a test says it for the browser.
async function phoneButtonWhenBrowserSays(page: Page, hybridTransport: boolean): Promise<Locator> {
  const other = await page.context().newPage();
  await other.addInitScript((supported) => {
    PublicKeyCredential.getClientCapabilities = () => Promise.resolve({ hybridTransport: supported });
  }, hybridTransport);
  await other.goto('/admin/passkeys');
  await expect(other.getByRole('heading', { level: 1, name: 'Passkeys' })).toBeVisible();
  return other.getByRole('button', { name: 'Add a phone' });
}

test('disables the phone button, and says why, when the browser says it cannot make a passkey on a phone', async ({
  page,
}) => {
  const button = await phoneButtonWhenBrowserSays(page, false);

  await expect(button).toBeDisabled();
  await expect(button.page().getByText('This browser cannot add a phone.')).toBeVisible();
  await button.page().close();
});

test('leaves the phone button when the browser says it can', async ({ page }) => {
  const button = await phoneButtonWhenBrowserSays(page, true);

  await expect(button).toBeEnabled();
  await button.page().close();
});

test('removes a passkey once a passkey that is left has confirmed it', async ({ page, scope }) => {
  await addPasskeyRow(scope.database, 'Spare');
  await openPasskeys(page);

  await row(page, 'Spare').getByRole('button', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'Remove Spare?' }).click();

  await expect(row(page, 'Spare')).toHaveCount(0);
  await expect(row(page, 'First key').getByRole('button', { name: 'Remove' })).toBeDisabled();
});
