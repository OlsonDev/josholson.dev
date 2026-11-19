import { expect, test } from '@playwright/test';

// These run first, against a site with no passkey yet (tests/support/api.ts starts one), and enroll nothing: the journey
// (journey.test.ts) is the one that does.

test('a site with no passkey offers setup, and not a sign-in', async ({ page }) => {
  await page.goto('/admin');

  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'Set up' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign in with a passkey' })).toHaveCount(0);
});

test('a wrong setup secret is refused, and nothing is registered', async ({ page }) => {
  await page.goto('/admin');

  await page.getByLabel('Setup secret').fill('not the setup secret');
  await page.getByLabel('Your name').fill('Nobody');
  await page.getByLabel('What to call this passkey').fill('Nobody');
  await page.getByRole('button', { name: 'Register passkey' }).click();

  await expect(page.getByRole('alert')).toContainText('not the setup secret, or setup is already done');
  await expect(page.getByRole('heading', { level: 2, name: 'Set up' })).toBeVisible();
});

test('says so when the API does not answer', async ({ page }) => {
  await page.route('**/auth/me', (route) => route.abort());
  await page.goto('/admin');

  await expect(page.getByRole('alert')).toContainText('The API is not answering');
});
