import { e2e, webOrigin } from './support/environment.ts';
import { expect, sendScope, test } from './support/scope.ts';
import { addVirtualAuthenticator } from './support/webauthn.ts';

// A site with no passkey yet: the `blank` layer is the schema with no rows, so setup is open (ADR 0044, the first passkey
// from the browser). Each test has a database of its own (ADR 0033, a scoped database per test), so the one that enrolls
// does not close setup for the others.
test.use({ layer: 'blank' });

test('sets up with the secret, which registers the first passkey and closes setup, then signs out and back in', async ({
  page,
  api,
  browser,
  scope,
}) => {
  const first = await addVirtualAuthenticator(page);
  await page.goto('/admin');
  await expect(page).toHaveURL(/\/admin\/sign-in$/);
  await page.getByLabel('Setup secret').fill(e2e().setupSecret);
  await page.getByLabel('Your name').fill('E2E Admin');
  await page.getByLabel('What to call this passkey').fill('First key');
  await page.getByRole('button', { name: 'Register passkey' }).click();

  await expect(page).toHaveURL(/\/admin$/);
  await expect(page.getByText('Signed in as E2E Admin')).toBeVisible();
  await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();
  expect(await first.credentialCount()).toBe(1);
  // The key keeps the name with the passkey, and a browser's chooser shows it: the nickname says which.
  expect(await first.userNames()).toEqual(['E2E Admin (First key)']);

  // Setup closed with the first passkey: another browser is offered a sign-in, and the secret no longer works.
  const stranger = await browser.newContext({ baseURL: webOrigin });
  await sendScope(stranger, scope.id);
  const strangerPage = await stranger.newPage();
  await strangerPage.goto('/admin');
  await expect(strangerPage.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  await expect(strangerPage.getByRole('heading', { level: 1, name: 'Set up' })).toHaveCount(0);
  const setup = await api.post('/auth/enrollment/setup', {
    data: { setupSecret: e2e().setupSecret, displayName: 'Nobody' },
  });
  expect(setup.status()).toBe(404);
  await stranger.close();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
  await expect(page.getByText('Signed in as E2E Admin')).toBeVisible();

  await page.getByRole('link', { name: 'Passkeys' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Passkeys' })).toBeVisible();
});

test('a site with no passkey sends the admin to setup, and not to a sign-in', async ({ page }) => {
  await page.goto('/admin');

  await expect(page).toHaveURL(/\/admin\/sign-in$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Set up' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign in with a passkey' })).toHaveCount(0);
});

test('a wrong setup secret is refused, and nothing is registered', async ({ page }) => {
  await page.goto('/admin/sign-in');

  await page.getByLabel('Setup secret').fill('not the setup secret');
  await page.getByLabel('Your name').fill('Nobody');
  await page.getByLabel('What to call this passkey').fill('Nobody');
  await page.getByRole('button', { name: 'Register passkey' }).click();

  await expect(page.getByRole('alert')).toContainText('not the setup secret, or setup is already done');
  await expect(page.getByRole('heading', { level: 1, name: 'Set up' })).toBeVisible();
});

test('says so when the API does not answer', async ({ page }) => {
  await page.route('**/auth/me', (route) => route.abort());
  await page.goto('/admin');

  await expect(page.getByRole('alert')).toContainText('The API is not answering');
});

test('every page of the admin area is behind the sign-in', async ({ page }) => {
  for (const path of [
    '/admin/inbox',
    '/admin/resumes',
    '/admin/skills',
    '/admin/projects',
    '/admin/blog',
    '/admin/tags',
    '/admin/media',
    '/admin/passkeys',
  ]) {
    await page.goto(path);
    await expect(page).toHaveURL(/\/admin\/sign-in$/);
  }
});
