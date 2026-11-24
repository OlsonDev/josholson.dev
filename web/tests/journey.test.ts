import { expect, type Locator, type Page, test } from '@playwright/test';

import { hexOf } from '../src/lib/palette/colors.ts';
import { apiUrl, e2e, webOrigin } from './support/environment.ts';
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

test('sets up with the secret, which registers the first passkey and closes setup, then signs out and back in', async () => {
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
  const stranger = await (await page.context().browser()!.newContext({ baseURL: webOrigin })).newPage();
  await stranger.goto('/admin');
  await expect(stranger.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  await expect(stranger.getByRole('heading', { level: 1, name: 'Set up' })).toHaveCount(0);
  const setup = await stranger.request.post(`${apiUrl}/auth/enrollment/setup`, {
    headers: { Origin: webOrigin },
    data: { setupSecret: e2e().setupSecret, displayName: 'Nobody' },
  });
  expect(setup.status()).toBe(404);
  await stranger.context().close();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
  await expect(page.getByText('Signed in as E2E Admin')).toBeVisible();

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
  expect(await second.userNames()).toEqual(['E2E Admin (Second key)']);
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

// The tags page, still signed in as the account the steps above made. The database is the `minimal` layer, which has the
// tags Database, Language, and Tool, so a test adds its own and removes it, and never counts the tags of the layer.
const rgbOf = (hex: string) =>
  `rgb(${[1, 3, 5].map((start) => Number.parseInt(hex.slice(start, start + 2), 16)).join(', ')})`;
// In the table: a tag's name is in the breadcrumbs too, as the page you are on.
const tagLink = (name: string) => page.getByRole('table').getByRole('link', { name, exact: true });
// Waits until what is animating (a popover zooming in, say) has finished, or been canceled by the next thing.
const animationsDone = () =>
  page.evaluate(() =>
    Promise.all(document.getAnimations().map((animation) => animation.finished.catch(() => undefined))),
  );
const saveButton = () => page.getByRole('button', { name: 'Save tag', exact: true });
const nameInput = () => page.getByLabel('Name', { exact: true });
const slugInput = () => page.getByLabel('Slug', { exact: true });

test('lists the tags in their colors, and paints them in the mirrored colors in dark mode', async () => {
  await page.goto('/admin/tags');
  await expect(page.getByRole('heading', { level: 1, name: 'Tags' })).toBeVisible();

  // Database is indigo-200 in the layer, and dark mode paints its mirror, indigo-800.
  await expect(tagLink('Database')).toHaveCSS('background-color', rgbOf(hexOf('indigo-200')));
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(tagLink('Database')).toHaveCSS('background-color', rgbOf(hexOf('indigo-800')));
  await page.emulateMedia({ colorScheme: 'light' });
  await expect(tagLink('Database')).toHaveCSS('background-color', rgbOf(hexOf('indigo-200')));
});

test('opens a tag beside the list, hides the list while it is open, and closes it with the back button', async () => {
  await tagLink('Language').click();

  await expect(page).toHaveURL(/\/admin\/tags\/2$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toBeFocused();
  // The list is still there, out of reach: under the pane, and neither focusable nor read.
  await expect(tagLink('Tool')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Delete', exact: true })).toBeVisible();

  await page.goBack();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toHaveCount(0);
  await expect(tagLink('Tool')).toBeVisible();
  // Focus goes back to what opened the pane.
  await expect(tagLink('Language')).toBeFocused();
});

test('counts the validation errors in the bar and focuses the first invalid field', async () => {
  await page.getByRole('link', { name: '+ Add tag' }).click();
  await expect(page).toHaveURL(/\/admin\/tags\/\+$/);

  // Name, slug, and color are all required: a color is not something a tag can go without, so it is not offered as an option.
  await expect(page.getByRole('button', { name: 'Color', exact: true })).toHaveText('None selected');
  await saveButton().click();
  await expect(page.getByText('Please address the 3 validation errors.')).toBeVisible();
  await expect(page.getByText('Color is required.')).toBeVisible();
  await expect(nameInput()).toBeFocused();

  // A name another tag has (ignoring case), and so the slug it suggests: both are taken, found before the API is asked.
  await nameInput().fill('DATABASE');
  await expect(slugInput()).toHaveValue('database');
  await saveButton().click();
  await expect(page.getByText('Name is already in use.')).toBeVisible();
  await expect(page.getByText('Slug is already in use.')).toBeVisible();
  await expect(page.getByText('Please address the 3 validation errors.')).toBeVisible();

  // Choosing a color clears its error, and the count follows.
  await page.getByRole('button', { name: 'Color', exact: true }).click();
  await page.getByRole('radio', { name: 'emerald-200', exact: true }).click();
  await expect(page.getByText('Color is required.')).toHaveCount(0);
  await nameInput().fill('Database Two');
  await slugInput().fill('database');
  await saveButton().click();
  await expect(page.getByText('Please address the validation error.')).toBeVisible();
  await expect(slugInput()).toBeFocused();
});

test('asks before leaving a form with changes, for Cancel and for the back button, and keeps them on request', async () => {
  await nameInput().fill('Unsaved');
  const dialog = page.getByRole('alertdialog', { name: 'Discard your changes?' });

  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toBeVisible();
  // The safe answer has the focus, so Enter keeps the work.
  await expect(dialog.getByRole('button', { name: 'Keep editing' })).toBeFocused();
  // Solid red, not a tint: a tinted background is written with an alpha, and a solid one is not.
  const discard = await dialog
    .getByRole('button', { name: 'Discard changes' })
    .evaluate((button) => getComputedStyle(button).backgroundColor);
  expect(discard).not.toContain('/');
  expect(discard).not.toBe('rgba(0, 0, 0, 0)');
  await dialog.getByRole('button', { name: 'Keep editing' }).click();
  await expect(dialog).toBeHidden();
  await expect(nameInput()).toHaveValue('Unsaved');

  await page.goBack();
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Keep editing' }).click();
  await expect(page).toHaveURL(/\/admin\/tags\/\+$/);

  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await dialog.getByRole('button', { name: 'Discard changes' }).click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(tagLink('Unsaved')).toHaveCount(0);
});

test('creates a tag: the slug follows the name until edited, and the color shows contrast in both modes', async () => {
  await page.getByRole('link', { name: '+ Add tag' }).click();

  await nameInput().fill('Test Tag');
  await expect(slugInput()).toHaveValue('test-tag');
  await slugInput().fill('test-tag-one');
  await nameInput().fill('Test Tag!');
  await expect(slugInput()).toHaveValue('test-tag-one');

  const colorButton = page.getByRole('button', { name: 'Color', exact: true });
  await expect(colorButton).toHaveText(/None selected/);
  await colorButton.click();
  await page.getByRole('radio', { name: 'emerald-200' }).hover();
  await expect(page.getByTestId('background-light')).toHaveText('emerald-200');
  await expect(page.getByTestId('background-dark')).toHaveText('emerald-800');
  await expect(page.getByTestId('contrast-legend')).toContainText(/AA\s*≥\s*4\.5/);
  await expect(page.getByTestId('contrast-legend')).toContainText(/AAA\s*≥\s*7\.0/);
  await expect(page.getByTestId('contrast-legend')).toContainText('Passes by ≤ 10%');
  // The panel is as tall as its tallest table and no taller: nothing is cut off, and there is little slack under it.
  const slack = await page.evaluate(() => {
    const panel = document.querySelector('[data-testid="contrast-panel"]')!;
    const bottom = Math.max(
      ...[...panel.querySelectorAll('tbody tr:last-child')].map((last) => last.getBoundingClientRect().bottom),
    );
    return { clipped: panel.scrollHeight > panel.clientHeight, slack: panel.getBoundingClientRect().bottom - bottom };
  });
  expect(slack.clipped).toBe(false);
  expect(slack.slack).toBeLessThan(4);
  await expect(page.getByRole('cell', { name: /^16\.4/ }).first()).toBeVisible();
  await page.getByRole('radio', { name: 'emerald-200' }).click();
  await expect(colorButton).toHaveText(/emerald-200/);

  await page.getByLabel('Description').fill('A **bold** note');
  await page.getByRole('button', { name: '+ Add alias' }).click();
  await page.getByRole('textbox', { name: 'Aliases' }).fill('ttag');
  await saveButton().click();

  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(tagLink('Test Tag!')).toHaveCSS('background-color', rgbOf(hexOf('emerald-200')));
  await expect(page.getByRole('row', { name: /Test Tag!/ })).toContainText('ttag');

  // The description shows, rendered, when the chip is hovered.
  await tagLink('Test Tag!').hover();
  await expect(page.locator('[data-slot="hover-card-content"] strong')).toHaveText('bold');
});

test('changing a slug keeps the old one redirecting, and removing it frees it', async () => {
  await tagLink('Test Tag!').click();
  await slugInput().fill('test-tag-two');
  await expect(page.getByText('keeps working: it redirects to the new one')).toBeVisible();
  await saveButton().click();
  await expect(page).toHaveURL(/\/admin\/tags$/);

  const moved = await page.request.get(`${apiUrl}/tags/by-slug/test-tag-one`, { maxRedirects: 0 });
  expect(moved.status()).toBe(308);
  expect(moved.headers()['location']).toBe('/tags/by-slug/test-tag-two');

  await tagLink('Test Tag!').click();
  await expect(page.getByText('Previous slugs')).toBeVisible();
  await page.getByRole('button', { name: 'Remove test-tag-one' }).click();
  await saveButton().click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  const gone = await page.request.get(`${apiUrl}/tags/by-slug/test-tag-one`, { maxRedirects: 0 });
  expect(gone.status()).toBe(404);
});

test('offers to reload or to overwrite when someone else saved the tag in between', async () => {
  await tagLink('Test Tag!').click();
  await expect(page).toHaveURL(/\/admin\/tags\/\d+$/);
  const tagId = Number(new URL(page.url()).pathname.split('/').at(-1));
  const current = await (await page.request.get(`${apiUrl}/tags/${tagId.toString()}`)).json();

  await nameInput().fill('Test Tag Mine');
  const theirs = await page.request.put(`${apiUrl}/tags/${tagId.toString()}`, {
    headers: { Origin: webOrigin },
    data: { ...current, name: 'Test Tag Theirs', expectedVersion: current.version },
  });
  expect(theirs.status()).toBe(204);

  await saveButton().click();
  await expect(page.getByText('Someone saved this tag after you opened it')).toBeVisible();
  await page.getByRole('button', { name: 'Save mine anyway' }).click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(tagLink('Test Tag Mine')).toBeVisible();
});

test('deletes a tag only after its slug is typed', async () => {
  await tagLink('Test Tag Mine').click();
  await page.getByRole('button', { name: 'Delete', exact: true }).click();

  const dialog = page.getByRole('alertdialog');
  await expect(dialog.getByText('Delete the tag “Test Tag Mine”?')).toBeVisible();
  const confirm = dialog.getByRole('button', { name: 'Delete tag' });
  await expect(confirm).toBeDisabled();
  await dialog.getByRole('textbox').fill('delete-test-tag-two');
  await expect(confirm).toBeEnabled();
  await confirm.click();

  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(tagLink('Test Tag Mine')).toHaveCount(0);
});

// Where each field is on the page, from the top, which should not change when a field is focused.
const fieldTops = () =>
  page.evaluate(() =>
    ['name', 'slug', 'color', 'description'].map(
      (field) => document.querySelector(`[data-field="${field}"]`)?.getBoundingClientRect().top,
    ),
  );

test('does not move the form when an input is focused', async () => {
  // Tall enough that the whole form is on screen: a field lower than the window is scrolled into view, which is not a shift.
  await page.setViewportSize({ width: 1280, height: 1400 });
  await page.goto('/admin/tags/+');
  await expect(nameInput()).toBeVisible();
  const before = await fieldTops();

  await nameInput().click();
  expect(await fieldTops()).toEqual(before);
  await slugInput().click();
  expect(await fieldTops()).toEqual(before);
  await page.getByLabel('Description').click();
  expect(await fieldTops()).toEqual(before);
  await page.setViewportSize({ width: 1280, height: 720 });
});

test('rings the swatch you are on, the same for the pointer and the keys, and marks the chosen color with a check', async () => {
  await page.goto('/admin/tags/+');
  const trigger = page.getByRole('button', { name: 'Color', exact: true });
  const swatch = (name: string) => page.getByRole('radio', { name, exact: true });
  const ring = (name: string) => swatch(name).evaluate((element) => getComputedStyle(element).boxShadow);
  await trigger.click();
  await animationsDone();

  // The pointer over a swatch rings it, above its neighbors, and no other swatch has one.
  await expect(swatch('red-50')).toHaveCSS('box-shadow', 'none');
  await swatch('red-50').hover();
  await expect(swatch('red-50')).toHaveCSS('z-index', '10');
  const byPointer = await ring('red-50');
  expect(byPointer).not.toBe('none');
  await expect(swatch('orange-50')).toHaveCSS('box-shadow', 'none');

  // The keys ring the one they land on, in the very same way, and the pointer's swatch lets go of it.
  await page.keyboard.press('ArrowRight');
  expect(await ring('orange-50')).toBe(byPointer);
  await expect(swatch('red-50')).toHaveCSS('box-shadow', 'none');

  // Black in light mode and white in dark mode: the ring's own color is what changes.
  await page.emulateMedia({ colorScheme: 'dark' });
  const darkSwatch = swatch('red-950');
  await expect(darkSwatch).toBeVisible();
  await darkSwatch.hover();
  expect(await ring('red-950')).not.toBe(byPointer);
  await page.emulateMedia({ colorScheme: 'light' });

  // The chosen color has a check, with or without a ring: it is not what the ring means.
  await swatch('red-50').click();
  await trigger.click();
  await animationsDone();
  await expect(swatch('red-50').locator('svg')).toBeVisible();
  await expect(swatch('orange-50').locator('svg')).toHaveCount(0);
  await swatch('orange-50').hover();
  await expect(swatch('red-50').locator('svg')).toBeVisible();
  await expect(swatch('red-50')).toHaveCSS('box-shadow', 'none');
  expect(await ring('orange-50')).not.toBe('none');
  await page.keyboard.press('Escape');
});

test('lets the pointer take over from the keys only once it crosses into another swatch', async () => {
  await page.goto('/admin/tags/+');
  await page.getByRole('button', { name: 'Color', exact: true }).click();
  await animationsDone();
  const swatch = (name: string) => page.getByRole('radio', { name, exact: true });
  const here = (await swatch('red-500').boundingBox())!;

  // The pointer rests on a swatch and the keys are used.
  await page.mouse.move(here.x + 4, here.y + 4);
  await expect(swatch('red-500')).toBeFocused();
  await swatch('olive-50').focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowDown');
  const keyed = page.locator('[role="radio"]:focus');
  const label = await keyed.getAttribute('aria-label');

  // Drifting all over the swatch it rests on, corner to corner, changes nothing.
  await page.mouse.move(here.x + 20, here.y + 20, { steps: 8 });
  await page.mouse.move(here.x + 2, here.y + 20, { steps: 8 });
  await expect(keyed).toHaveAttribute('aria-label', label!);
  // Crossing into the next swatch takes over.
  await page.mouse.move(here.x + 40, here.y + 12, { steps: 8 });
  await expect(swatch('red-500')).not.toBeFocused();
  await expect(page.locator('[role="radio"]:focus')).not.toHaveAttribute('aria-label', label!);

  // Leaving the palette and coming back into the swatch it rested on counts as crossing, too.
  await page.mouse.move(here.x + 4, here.y + 4);
  await expect(swatch('red-500')).toBeFocused();
  await page.keyboard.press('ArrowUp');
  await expect(swatch('red-400')).toBeFocused();
  await page.mouse.move(here.x + 10, here.y + 10, { steps: 4 });
  await expect(swatch('red-400')).toBeFocused();
  await page.getByText('Weights for light mode').hover();
  await page.mouse.move(here.x + 6, here.y + 6, { steps: 4 });
  await expect(swatch('red-500')).toBeFocused();
  await page.keyboard.press('Escape');
});

test('moves through the palette with the arrow keys, and a sideways move into black or white undoes itself', async () => {
  await page.goto('/admin/tags/+');
  await page.getByRole('button', { name: 'Color', exact: true }).click();
  await animationsDone();
  const swatch = (name: string) => page.getByRole('radio', { name, exact: true });

  // Light mode: white beside the first three rows (50 to 200) and black beside the last three (300 to 500).
  // In from a row and straight back out lands on the same row, at the top, the middle, and the bottom of each.
  for (const [start, extreme] of [
    ['olive-50', 'white'],
    ['olive-100', 'white'],
    ['olive-200', 'white'],
    ['olive-300', 'black'],
    ['olive-400', 'black'],
    ['olive-500', 'black'],
  ] as const) {
    await swatch(start).focus();
    await page.keyboard.press('ArrowRight');
    await expect(swatch(extreme)).toBeFocused();
    await page.keyboard.press('ArrowLeft');
    await expect(swatch(start)).toBeFocused();
  }

  // A move up or down inside the tall column picks a new place in it, and left goes back to the middle of the three rows.
  await swatch('olive-50').focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowDown');
  await expect(swatch('black')).toBeFocused();
  await page.keyboard.press('ArrowLeft');
  await expect(swatch('olive-400')).toBeFocused();

  await swatch('olive-500').focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowUp');
  await expect(swatch('white')).toBeFocused();
  await page.keyboard.press('ArrowLeft');
  await expect(swatch('olive-100')).toBeFocused();
  await page.keyboard.press('Escape');
});

test('starts the keys at the choice, and a required color has no "No color" to go to', async () => {
  await page.goto('/admin/tags/+');
  const trigger = page.getByRole('button', { name: 'Color', exact: true });
  const swatch = (name: string) => page.getByRole('radio', { name, exact: true });

  // Nothing chosen: the picker opens on the palette with no swatch active, there is no "No color", and a key goes to the
  // first swatch.
  await trigger.click();
  await animationsDone();
  await expect(page.getByRole('button', { name: 'No color' })).toHaveCount(0);
  await expect(page.getByRole('radiogroup', { name: 'Color' })).toBeFocused();
  await expect(page.getByTestId('contrast-panel')).toContainText('Hover or focus a color');
  await page.keyboard.press('ArrowDown');
  await expect(swatch('red-50')).toBeFocused();
  await page.keyboard.press('Escape');

  // Choose a color, and the picker opens on it, with a check, so a second Space picks the same one.
  await trigger.click();
  await swatch('emerald-200').click();
  await expect(trigger).toHaveText(/emerald-200/);
  await trigger.click();
  await animationsDone();
  await expect(swatch('emerald-200')).toBeFocused();
  await expect(swatch('emerald-200').locator('svg')).toBeVisible();

  // Up goes up the weights and stops at the top row: there is nothing above it.
  await page.keyboard.press('ArrowUp');
  await page.keyboard.press('ArrowUp');
  await expect(swatch('emerald-50')).toBeFocused();
  await page.keyboard.press('ArrowUp');
  await expect(swatch('emerald-50')).toBeFocused();

  // Space on the swatch the picker opened on keeps the choice.
  await swatch('emerald-200').focus();
  await page.keyboard.press('Space');
  await expect(trigger).toHaveText(/emerald-200/);
});

test('shows an unchosen color dimmed with no swatch, and a chosen one with its swatch and name', async () => {
  await page.goto('/admin/tags/+');
  const trigger = page.getByRole('button', { name: 'Color', exact: true });
  await expect(trigger).toHaveText('None selected');
  await expect(trigger.locator('span.border')).toHaveCount(0);
  const dimmed = await trigger.getByText('None selected').evaluate((element) => getComputedStyle(element).color);
  const plain = await page.getByLabel('Name', { exact: true }).evaluate((element) => getComputedStyle(element).color);
  expect(dimmed).not.toBe(plain);

  await trigger.click();
  await page.getByRole('radio', { name: 'emerald-200', exact: true }).click();
  await expect(trigger.locator('span.border')).toHaveCount(1);
  await expect(trigger.locator('code')).toHaveText('emerald-200');
});

test('keeps the swatch it came from while the pointer crosses the gaps between swatches', async () => {
  await page.goto('/admin/tags/+');
  await page.getByRole('button', { name: 'Color', exact: true }).click();
  await animationsDone();
  const left = (await page.getByRole('radio', { name: 'red-50', exact: true }).boundingBox())!;
  const right = (await page.getByRole('radio', { name: 'orange-50', exact: true }).boundingBox())!;
  const background = page.getByTestId('background-light');

  await page.mouse.move(left.x + left.width / 2, left.y + left.height / 2);
  await expect(background).toHaveText('red-50');
  // Into the gap between the two, and out of it onto the right.
  const gap = (left.x + left.width + right.x) / 2;
  await page.mouse.move(gap, left.y + left.height / 2);
  await expect(background).toHaveText('red-50');
  await page.mouse.move(right.x + right.width / 2, right.y + right.height / 2);
  await expect(background).toHaveText('orange-50');
  // Leaving the grid (onto the header above it) changes nothing: the place stays where the pointer last put it.
  await page.getByText('Weights for light mode').hover();
  await expect(page.getByRole('radio', { name: 'orange-50', exact: true })).toBeFocused();
  await expect(background).toHaveText('orange-50');
  // After a key press the keys own the place, and leaving does not take it back either.
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('radio', { name: 'amber-50', exact: true })).toBeFocused();
  await page.mouse.move(left.x + left.width / 2, left.y + left.height / 2);
  await page.getByText('Weights for light mode').hover();
  await expect(background).toHaveText('red-50');
  await page.keyboard.press('Escape');
});

test('puts black and white in one column at the right, three rows each, whichever is at the top of the scale on top', async () => {
  await page.getByRole('button', { name: 'Color', exact: true }).click();
  // The popover zooms in as it opens, which moves every box a little until it is done.
  await animationsDone();
  const box = (name: string) => page.getByRole('radio', { name, exact: true }).boundingBox();
  const lastFamily = await box('red-50');
  const reddest = lastFamily!;

  // Light mode starts at 50, so white is on top, and each is three swatches (and the gaps) tall.
  const white = (await box('white'))!;
  const black = (await box('black'))!;
  expect(white.y).toBeLessThan(black.y);
  expect(white.x).toBeGreaterThan(reddest.x + 25 * 24);
  expect(white.x).toBe(black.x);
  expect(white.height).toBeCloseTo(3 * 24 + 2 * 2, 0);
  // The same 2px gap as between any two swatches, across and down.
  const olive = (await box('olive-50'))!;
  expect(white.x - (olive.x + olive.width)).toBeCloseTo(2, 0);
  expect(black.y - (white.y + white.height)).toBeCloseTo(2, 0);
  expect(black.height).toBeCloseTo(3 * 24 + 2 * 2, 0);

  // Dark mode starts at 950, so what is painted black is on top. The swatch that stores white is painted black there.
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page.getByRole('radio', { name: 'red-950', exact: true })).toBeVisible();
  const darkTop = (await box('black'))!;
  const darkBottom = (await box('white'))!;
  expect(darkTop.y).toBeLessThan(darkBottom.y);

  await page.emulateMedia({ colorScheme: 'light' });
  await page.keyboard.press('Escape');
});

test('keeps the contrast panel the same size as the pointer moves from color to color', async () => {
  await page.getByRole('button', { name: 'Color', exact: true }).click();
  const panel = page.locator('[data-testid="contrast-panel"]');
  const shape = () =>
    page.evaluate(() => ({
      popover: document.querySelector('[data-slot="popover-content"]')?.getBoundingClientRect().toJSON(),
      panel: document.querySelector('[data-testid="contrast-panel"]')?.getBoundingClientRect().toJSON(),
      headers: [...document.querySelectorAll('[data-testid="contrast-panel"] th')].map((th) =>
        th.getBoundingClientRect().toJSON(),
      ),
    }));

  await page.getByRole('radio', { name: 'red-50', exact: true }).hover();
  await expect(panel.getByTestId('background-light')).toHaveText('red-50');
  const reference = await shape();
  // A long family name, a dark weight, and black, which has one row where the others have five.
  for (const swatch of ['fuchsia-500', 'slate-300', 'black']) {
    await page.getByRole('radio', { name: swatch, exact: true }).hover();
    await expect(panel.getByTestId('background-light')).toHaveText(swatch);
    expect(await shape()).toEqual(reference);
  }
  // Only as wide as its content: the swatches, and not the browser.
  expect(reference.popover!.width).toBeLessThan(900);
  await page.keyboard.press('Escape');
});

test('spaces the parts of the color picker evenly, from the top edge to the bottom one', async () => {
  // Tall enough that the popover is not cut off and scrolling, so its bottom edge is where it ends.
  await page.setViewportSize({ width: 1280, height: 1200 });
  await page.goto('/admin/tags/+');
  await page.getByRole('button', { name: 'Color', exact: true }).click();
  await page.getByRole('radio', { name: 'red-50', exact: true }).hover();
  await animationsDone();

  const spacing = await page.evaluate(() => {
    const popover = document.querySelector('[data-slot="popover-content"]')!;
    const parts = [...popover.children].map((child) => child.getBoundingClientRect());
    const box = popover.getBoundingClientRect();
    return {
      top: parts[0].top - box.top,
      between: parts.slice(1).map((part, index) => part.top - parts[index].bottom),
      bottom: box.bottom - parts.at(-1).bottom,
    };
  });

  // The edge, the header, the palette, a rule, the analysis, a rule, the legend, the edge: one distance throughout.
  const distances = [spacing.top, ...spacing.between, spacing.bottom];
  expect(distances).toHaveLength(7);
  for (const distance of distances) expect(distance).toBeCloseTo(distances[0], 0);
  await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 1280, height: 720 });
});

// Where the help text under each input is, from the top: the distance from an input to the text under it is what a password
// manager's icon can change, by adding an element beside the input.
const helpTops = () =>
  page.evaluate(() =>
    ['name', 'slug'].map((field) => {
      const holder = document.querySelector(`[data-field="${field}"]`)!;
      return [
        holder.querySelector('input')!.getBoundingClientRect().height,
        holder.querySelector('p')!.getBoundingClientRect().top -
          holder.querySelector('input')!.getBoundingClientRect().bottom,
      ];
    }),
  );

test('does not change the distance from an input to its help text when it is focused, or when an extension adds an element beside it', async () => {
  await page.setViewportSize({ width: 1280, height: 1400 });
  await page.goto('/admin/tags/+');
  await expect(nameInput()).toBeVisible();
  const before = await helpTops();

  await nameInput().click();
  expect(await helpTops()).toEqual(before);
  await page.keyboard.press('Tab');
  await expect(slugInput()).toBeFocused();
  expect(await helpTops()).toEqual(before);
  await page.keyboard.press('Shift+Tab');
  await expect(nameInput()).toBeFocused();
  expect(await helpTops()).toEqual(before);

  // What a password manager does when an input is focused: puts an element of its own next to the input, in the same
  // parent, whether or not it has a size. Nothing about the page should move for it.
  for (const style of ['', 'position: absolute; right: 8px; width: 16px; height: 16px;']) {
    await page.evaluate((css) => {
      const input = document.querySelector('[data-field="name"] input')!;
      const injected = document.createElement('div');
      injected.setAttribute('data-test-injected', '');
      injected.style.cssText = css;
      input.after(injected);
    }, style);
    expect(await helpTops()).toEqual(before);
    await page.evaluate(() => document.querySelector('[data-test-injected]')!.remove());
  }
  await page.setViewportSize({ width: 1280, height: 720 });
});

test('orders the bar as Save, Cancel, a divider and the message, and Delete at the far right', async () => {
  await page.goto('/admin/tags/2');
  await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toBeVisible();
  await nameInput().fill('Language!');
  await expect(page.getByText('You have unsaved changes.')).toBeVisible();

  const left = (name: string) =>
    page
      .getByRole('button', { name, exact: true })
      .boundingBox()
      .then((box) => box!.x);
  const save = await left('Save tag');
  const cancel = await left('Cancel');
  const message = (await page.getByText('You have unsaved changes.').boundingBox())!.x;
  const remove = await left('Delete');
  expect(save).toBeLessThan(cancel);
  expect(cancel).toBeLessThan(message);
  expect(message).toBeLessThan(remove);
  // Delete is at the right edge of the bar, with the message's room between it and Cancel.
  const bar = (await page.getByRole('button', { name: 'Delete', exact: true }).boundingBox())!;
  expect(bar.x + bar.width).toBeGreaterThan(1280 - 80);

  await nameInput().fill('Language');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
});

test('pages the list by the address, and a tag opened from a page returns to it', async () => {
  const listed: unknown = await (await page.request.get(`${apiUrl}/tags`)).json();
  const existing = Array.isArray(listed) ? listed.length : 0;
  const made: number[] = [];
  for (let n = 1; n <= 12; n++) {
    const slug = `zz-page-${n.toString().padStart(2, '0')}`;
    const response = await page.request.post(`${apiUrl}/tags`, {
      headers: { Origin: webOrigin },
      data: {
        name: slug,
        slug,
        description: null,
        color: 'emerald-200',
        aliases: [],
        previousSlugs: [],
        expectedVersion: null,
      },
    });
    made.push(Number(response.headers()['location'].split('/').at(-1)));
  }
  const total = existing + made.length;
  const summary = page.getByTestId('paging-summary');
  try {
    await page.goto('/admin/tags');
    await expect(summary).toHaveText(`1–10 of ${total.toString()} tags`);
    await expect(page.getByTestId('tag-row')).toHaveCount(10);
    await expect(page.getByRole('button', { name: 'Previous page' })).toBeDisabled();
    // Where the pages, the summary, and the page size are, which clicking through the pages must not change.
    const layout = () =>
      page.evaluate(() => ({
        pages: [...document.querySelectorAll('nav[aria-label="Pages"] > *')].map((element) =>
          element.getBoundingClientRect().toJSON(),
        ),
        summary: document.querySelector('[data-testid="paging-summary"]')?.getBoundingClientRect().toJSON(),
        range: document.querySelector('[data-testid="paging-summary"] span')?.getBoundingClientRect().toJSON(),
        size: document.querySelector('[data-bar="list"] select')?.getBoundingClientRect().toJSON(),
        add: document.querySelector('[data-bar="list"] a[href^="/admin/tags/+"]')?.getBoundingClientRect().toJSON(),
      }));
    // The chevrons are the same square, and as big as the page numbers beside them (one digit is the same 32px square).
    const previous = (await page.getByRole('button', { name: 'Previous page' }).boundingBox())!;
    const next = (await page.getByRole('link', { name: 'Next page' }).boundingBox())!;
    expect(previous.width).toBe(next.width);
    expect(previous.height).toBe(next.height);
    const one = (await page.getByRole('link', { name: 'Page 1' }).boundingBox())!;
    expect(one.height).toBe(previous.height);
    expect(one.width).toBe(previous.width);
    const onFirst = await layout();

    // Next is a link to ?page=2, and the page is in the address, so a refresh stays on it.
    await page.getByRole('link', { name: 'Next page' }).click();
    await expect(page).toHaveURL(/\/admin\/tags\?page=2$/);
    await animationsDone();
    expect(await layout()).toEqual(onFirst);
    await expect(summary).toHaveText(`11–${total.toString()} of ${total.toString()} tags`);
    await expect(page.getByRole('link', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page');
    await page.reload();
    await expect(summary).toHaveText(`11–${total.toString()} of ${total.toString()} tags`);

    // "+ Add tag" and a tag open beside the list at the same page, and closing returns to it.
    await expect(page.getByRole('link', { name: '+ Add tag' })).toHaveAttribute('href', '/admin/tags/+?page=2');
    await page.getByRole('link', { name: 'zz-page-12', exact: true }).first().click();
    await expect(page).toHaveURL(new RegExp(`/admin/tags/${made.at(-1)!.toString()}\\?page=2$`));
    await expect(page.getByRole('heading', { level: 1, name: 'zz-page-12' })).toBeVisible();
    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(page).toHaveURL(/\/admin\/tags\?page=2$/);
    await expect(summary).toHaveText(`11–${total.toString()} of ${total.toString()} tags`);

    // Another size keeps the first row showing on screen, and a page past the end is the last one.
    await page.getByLabel('Per page').selectOption('25');
    await expect(page).toHaveURL(/\/admin\/tags\?size=25$/);
    await expect(summary).toHaveText(`1–${total.toString()} of ${total.toString()} tags`);
    await page.goto('/admin/tags?page=9');
    await expect(summary).toHaveText(`11–${total.toString()} of ${total.toString()} tags`);
  } finally {
    for (const tagId of made)
      await page.request.delete(`${apiUrl}/tags/${tagId.toString()}`, { headers: { Origin: webOrigin } });
  }
});

test('has a section of the tag for what uses it, at an address of its own, that keeps the form while it is open', async () => {
  await page.goto('/admin/tags/2/used-by');
  await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toBeVisible();
  const sections = page.getByRole('navigation', { name: 'Sections of the tag' });
  await expect(sections.getByRole('link', { name: 'Used by' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByText('Nothing uses this tag yet.')).toBeVisible();
  await expect(nameInput()).toBeHidden();

  // A refresh stays on the section, and the other one is a link back.
  await page.reload();
  await expect(page.getByText('Nothing uses this tag yet.')).toBeVisible();
  await sections.getByRole('link', { name: 'Details' }).click();
  await expect(page).toHaveURL(/\/admin\/tags\/2$/);
  await expect(nameInput()).toBeVisible();

  // Typing, and moving between the sections, is not leaving: no question, and what was typed is still there.
  await nameInput().fill('Language!');
  await sections.getByRole('link', { name: 'Used by' }).click();
  await expect(page).toHaveURL(/\/admin\/tags\/2\/used-by$/);
  await expect(page.getByRole('alertdialog')).toHaveCount(0);
  await sections.getByRole('link', { name: 'Details' }).click();
  await expect(nameInput()).toHaveValue('Language!');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Discard changes' }).click();
  await expect(page).toHaveURL(/\/admin\/tags$/);

  // A new tag has nothing that uses it, so its section goes back to the form.
  await page.goto('/admin/tags/+/used-by');
  await expect(page).toHaveURL(/\/admin\/tags\/\+$/);
});

test('draws the page in the mode it is in, with thin scrollbars that keep their room, and bars that the scrollbar stops above', async () => {
  await page.goto('/admin/tags/2');
  await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toBeVisible();
  const scheme = () => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme);

  expect(await scheme()).toBe('light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect.poll(scheme).toBe('dark');
  await page.emulateMedia({ colorScheme: 'light' });

  const pane = page.locator('[data-pane-scroll]');
  await expect(pane).toHaveCSS('scrollbar-gutter', 'stable both-edges');
  await expect(pane).toHaveCSS('scrollbar-width', 'thin');

  // The section does not move sideways when the other one is shorter (no scrollbar) or longer (one).
  const headingX = () =>
    page.getByRole('heading', { level: 1, name: 'Language' }).evaluate((element) => element.getBoundingClientRect().x);
  const onDetails = await headingX();
  await page.getByRole('navigation', { name: 'Sections of the tag' }).getByRole('link', { name: 'Used by' }).click();
  await expect(page).toHaveURL(/used-by$/);
  expect(await headingX()).toBe(onDetails);

  // The scrolling part ends where the bar begins, in the pane and in the list.
  const above = (scroller: string, bar: string) =>
    page.evaluate(
      ([scrollerSelector, barSelector]) =>
        document.querySelector(scrollerSelector)!.getBoundingClientRect().bottom <=
        document.querySelector(barSelector)!.getBoundingClientRect().top + 1,
      [scroller, bar],
    );
  expect(await above('[data-pane-scroll]', '[data-bar="pane"]')).toBe(true);
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(page.locator('[data-bar="list"]')).toBeVisible();
  await animationsDone();
  expect(await above('[data-list-scroll]', '[data-bar="list"]')).toBe(true);
});

test('puts the tag in the breadcrumbs, with the list one step back at the page it was on', async () => {
  // The links are the steps back: the page you are on is marked as a link too, but is not one.
  const crumbs = page.getByRole('navigation', { name: 'breadcrumb' });

  await page.goto('/admin/tags');
  await expect(crumbs.locator('a')).toHaveText(['Admin']);
  await expect(crumbs.getByText('Tags', { exact: true })).toHaveAttribute('aria-current', 'page');

  await page.goto('/admin/tags/2?page=1&size=25');
  await expect(crumbs.locator('a')).toHaveText(['Admin', 'Tags']);
  await expect(crumbs.locator('a', { hasText: 'Tags' })).toHaveAttribute('href', '/admin/tags?size=25');
  await expect(crumbs.getByText('Language', { exact: true })).toHaveAttribute('aria-current', 'page');

  // The section of the tag is still the tag, and a new tag is "New tag".
  await page.goto('/admin/tags/2/used-by');
  await expect(crumbs.getByText('Language', { exact: true })).toHaveAttribute('aria-current', 'page');
  await page.goto('/admin/tags/+');
  await expect(crumbs.getByText('New tag', { exact: true })).toHaveAttribute('aria-current', 'page');

  // One step back is the crumb next to it, and with changes in the form it asks first, like any other way out.
  await nameInput().fill('Unsaved');
  await crumbs.locator('a', { hasText: 'Tags' }).click();
  await expect(page.getByRole('alertdialog', { name: 'Discard your changes?' })).toBeVisible();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Discard changes' }).click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(crumbs.locator('a')).toHaveText(['Admin']);
});

test('has the color of the mode in the page before any style has arrived, so a refresh does not flash white', async () => {
  // The page as the server sends it, before any stylesheet or script: a page drawn in the browser has no styles until its
  // script has run, and this is all that holds the color until then.
  const html = await (await page.request.get('/admin/tags/2/used-by')).text();
  expect(html).toContain('<meta name="color-scheme" content="light dark" />');
  expect(html).toMatch(/html\s*\{\s*background:\s*oklch\(1 0 0\)/);
  expect(html).toMatch(/html\.dark\s*\{\s*background:\s*oklch\(0\.145 0 0\)/);

  // And the styles give each mode a color of its own, which the page follows as the system changes.
  await page.goto('/admin/tags/2/used-by');
  const background = () => page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  await page.emulateMedia({ colorScheme: 'light' });
  await expect.poll(background).toBe('oklch(1 0 0)');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect.poll(background).not.toBe('oklch(1 0 0)');
  await page.emulateMedia({ colorScheme: 'light' });
});
