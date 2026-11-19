import { expect, test, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// What tests/support/api.ts made for this run: the API's URL, and the one enrollment token it minted.
function session(): { enrollmentToken: string } {
  return JSON.parse(readFileSync(join(import.meta.dirname, '..', '..', '.tmp', 'e2e.json'), 'utf8'));
}

// A passkey the browser makes up, so no key has to be touched: Chromium's virtual authenticator, which behaves like a
// security key with a PIN that is always right and a touch that always happens.
async function useVirtualAuthenticator(page: Page): Promise<() => Promise<number>> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'usb',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
  return async () => (await cdp.send('WebAuthn.getCredentials', { authenticatorId })).credentials.length;
}

test('a visitor who is not signed in is offered a passkey sign-in', async ({ page }) => {
  await page.goto('/admin');

  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign in with a passkey' })).toBeVisible();
});

test('a link with a token is the page for registering a passkey, and a bad token is refused', async ({ page }) => {
  await useVirtualAuthenticator(page);
  await page.goto('/admin#token=not-a-real-token');

  await expect(page.getByRole('heading', { level: 2, name: 'Register a passkey' })).toBeVisible();
  await page.getByLabel('What to call it').fill('Nobody');
  await page.getByRole('button', { name: 'Register passkey' }).click();

  await expect(page.getByRole('alert')).toContainText('expired or was already used');
});

test('says so when the API does not answer', async ({ page }) => {
  await page.route('**/auth/me', (route) => route.abort());
  await page.goto('/admin');

  await expect(page.getByRole('alert')).toContainText('The API is not answering');
});

test('registers a passkey from the link, signs out, and signs back in with it', async ({ page }) => {
  const credentialCount = await useVirtualAuthenticator(page);

  await page.goto(`/admin#token=${session().enrollmentToken}`);
  await page.getByLabel('What to call it').fill('Test key');
  await page.getByRole('button', { name: 'Register passkey' }).click();

  await expect(page.getByText('Signed in as E2E Admin.')).toBeVisible();
  expect(page.url()).not.toContain('token=');
  expect(await credentialCount()).toBe(1);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
  await expect(page.getByText('Signed in as E2E Admin.')).toBeVisible();
});
