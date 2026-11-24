import { account } from './support/account.generated.ts';
import { apiUrl } from './support/environment.ts';
import { expect, scopeHeader, test } from './support/scope.ts';
import { addVirtualAuthenticator } from './support/webauthn.ts';

// The layers a test starts the admin from (ADR 0033, a scoped database per test): `blank`, with no account, so setup is
// open; `account`, with the owner and one passkey, so the admin asks for a sign-in; and the account signed in, which opens
// any page of the admin directly.

test.describe('the blank layer', () => {
  test.use({ layer: 'blank' });

  test('has no account, so the admin offers setup, and nothing in it', async ({ page, request, scope }) => {
    await page.goto('/admin');
    const tags: unknown = await (await request.get(`${apiUrl}/tags`, { headers: { [scopeHeader]: scope.id } })).json();

    await expect(page).toHaveURL(/\/admin\/sign-in$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Set up' })).toBeVisible();
    expect(tags).toEqual([]);
  });
});

test.describe('the account layer', () => {
  test.use({ layer: 'account' });

  test('has a passkey, so setup is closed and the admin asks for a sign-in', async ({ page }) => {
    await page.goto('/admin');

    await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: 'Set up' })).toHaveCount(0);
  });

  test('signs in with the passkey in the layer, by the real ceremony', async ({ page }) => {
    const authenticator = await addVirtualAuthenticator(page, { accountPasskey: true });
    await page.goto('/admin');

    await page.getByRole('button', { name: 'Sign in with a passkey' }).click();

    await expect(page.getByText(`Signed in as ${account.displayName}`)).toBeVisible();
    expect(await authenticator.credentialCount()).toBe(1);
  });

  test.describe('signed in', () => {
    test.use({ signedIn: true });

    test('opens a page of the admin directly, with no sign-in first', async ({ page }) => {
      await page.goto('/admin/tags');

      await expect(page).toHaveURL(/\/admin\/tags$/);
      await expect(page.getByText(`Signed in as ${account.displayName}`)).toBeVisible();
    });

    test('lists the passkey of the account', async ({ page }) => {
      await page.goto('/admin/passkeys');

      await expect(page.getByRole('heading', { level: 1, name: 'Passkeys' })).toBeVisible();
      await expect(page.getByRole('rowheader', { name: account.nickname, exact: true })).toBeVisible();
    });
  });
});

// The three tags Database, Language, and Tool, one block of IDs above the account's (the layer is the account's child), so
// a test that opens a tag by its address names the ID the layer gives it.
test.describe('the some-tags layer', () => {
  test.use({ layer: 'some-tags', signedIn: true });

  test('has the account and the first three tags, Language being 1002', async ({ page }) => {
    await page.goto('/admin/tags/1002');

    await expect(page.getByText(`Signed in as ${account.displayName}`)).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toBeVisible();
  });
});
