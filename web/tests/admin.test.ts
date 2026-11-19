import { expect, test } from '@playwright/test';
import { addVirtualAuthenticator } from './support/webauthn.ts';

test('a visitor who is not signed in is offered a passkey sign-in', async ({ page }) => {
  await page.goto('/admin');

  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign in with a passkey' })).toBeVisible();
});

test('a link with a token is the page for registering a passkey, and a bad token is refused', async ({ page }) => {
  await addVirtualAuthenticator(page);
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
