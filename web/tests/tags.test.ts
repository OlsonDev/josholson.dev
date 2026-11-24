import { hexOf } from '../src/lib/palette/colors.ts';
import { rgbOf, tagPage } from './support/admin.ts';
import { expect, test } from './support/scope.ts';

// The tags page: the list, the form beside it, and what a save, a conflict, and a delete do. Each test has the account
// signed in on the `some-tags` layer (Database, Language, and Tool), makes the tags it needs, and none depends on another. A
// test never counts the tags of the layer.

test.use({ layer: 'some-tags', signedIn: true });

test('lists the tags in their colors, and paints them in the mirrored colors in dark mode', async ({ page }) => {
  const { tagLink } = tagPage(page);
  await page.goto('/admin/tags');
  await expect(page.getByRole('heading', { level: 1, name: 'Tags' })).toBeVisible();

  // Database is indigo-200 in the layer, and dark mode paints its mirror, indigo-800.
  await expect(tagLink('Database')).toHaveCSS('background-color', rgbOf(hexOf('indigo-200')));
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(tagLink('Database')).toHaveCSS('background-color', rgbOf(hexOf('indigo-800')));
  await page.emulateMedia({ colorScheme: 'light' });
  await expect(tagLink('Database')).toHaveCSS('background-color', rgbOf(hexOf('indigo-200')));
});

test('opens a tag beside the list, hides the list while it is open, and closes it with the back button', async ({
  page,
}) => {
  const { tagLink } = tagPage(page);
  await page.goto('/admin/tags');
  await tagLink('Language').click();

  await expect(page).toHaveURL(/\/admin\/tags\/1002$/);
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

test('counts the validation errors in the bar and focuses the first invalid field', async ({ page }) => {
  const { saveButton, nameInput, slugInput } = tagPage(page);
  await page.goto('/admin/tags');
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

test('asks before leaving a form with changes, for Cancel and for the back button, and keeps them on request', async ({
  page,
}) => {
  const { tagLink, nameInput } = tagPage(page);
  await page.goto('/admin/tags');
  await page.getByRole('link', { name: '+ Add tag' }).click();
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

test('creates a tag: the slug follows the name until edited, and the color shows contrast in both modes', async ({
  page,
}) => {
  const { tagLink, saveButton, nameInput, slugInput } = tagPage(page);
  await page.goto('/admin/tags');
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

// The tests from here change or delete the tag Tool, which the layer gives (slug `tool`): the database is the test's own, so
// there is nothing to put back.
test('changing a slug keeps the old one redirecting, and removing it frees it', async ({ page, api }) => {
  const { tagLink, saveButton, slugInput } = tagPage(page);
  await page.goto('/admin/tags');
  await tagLink('Tool').click();
  await slugInput().fill('tool-two');
  await expect(page.getByText('keeps working: it redirects to the new one')).toBeVisible();
  await saveButton().click();
  await expect(page).toHaveURL(/\/admin\/tags$/);

  const moved = await api.get('/tags/by-slug/tool', { maxRedirects: 0 });
  expect(moved.status()).toBe(308);
  expect(moved.headers()['location']).toBe('/tags/by-slug/tool-two');

  await tagLink('Tool').click();
  await expect(page.getByText('Previous slugs')).toBeVisible();
  await page.getByRole('button', { name: 'Remove tool' }).click();
  await saveButton().click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  const gone = await api.get('/tags/by-slug/tool', { maxRedirects: 0 });
  expect(gone.status()).toBe(404);
});

test('offers to reload or to overwrite when someone else saved the tag in between', async ({ page, api }) => {
  const { tagLink, saveButton, nameInput } = tagPage(page);
  await page.goto('/admin/tags');
  await tagLink('Tool').click();
  await expect(page).toHaveURL(/\/admin\/tags\/\d+$/);
  const tagId = Number(new URL(page.url()).pathname.split('/').at(-1));
  const current = await (await api.get(`/tags/${tagId.toString()}`)).json();

  await nameInput().fill('Tool Mine');
  const theirs = await api.put(`/tags/${tagId.toString()}`, {
    data: { ...current, name: 'Tool Theirs', expectedVersion: current.version },
  });
  expect(theirs.status()).toBe(204);

  await saveButton().click();
  await expect(page.getByText('Someone saved this tag after you opened it')).toBeVisible();
  await page.getByRole('button', { name: 'Save mine anyway' }).click();
  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(tagLink('Tool Mine')).toBeVisible();
});

test('deletes a tag only after its slug is typed', async ({ page }) => {
  const { tagLink } = tagPage(page);
  await page.goto('/admin/tags');
  await tagLink('Tool').click();
  await page.getByRole('button', { name: 'Delete', exact: true }).click();

  const dialog = page.getByRole('alertdialog');
  await expect(dialog.getByText('Delete the tag “Tool”?')).toBeVisible();
  const confirm = dialog.getByRole('button', { name: 'Delete tag' });
  await expect(confirm).toBeDisabled();
  await dialog.getByRole('textbox').fill('delete-tool');
  await expect(confirm).toBeEnabled();
  await confirm.click();

  await expect(page).toHaveURL(/\/admin\/tags$/);
  await expect(tagLink('Tool')).toHaveCount(0);
});
