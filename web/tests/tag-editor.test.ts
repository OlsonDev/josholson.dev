import { tagPage } from './support/admin.ts';
import { expect, test } from './support/scope.ts';

// How the tag editor and its page are laid out, and the parts around them (breadcrumbs, the section for what uses a tag,
// the page's color). Each test has the account signed in on the `some-tags` layer, where Language is tag 1002, and none
// depends on another.

test.use({ layer: 'some-tags', signedIn: true });

test('does not move the form when an input is focused', async ({ page }) => {
  const { nameInput, slugInput, fieldTops } = tagPage(page);
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

test('does not change the distance from an input to its help text when it is focused, or when an extension adds an element beside it', async ({
  page,
}) => {
  const { nameInput, slugInput, helpTops } = tagPage(page);
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

test('orders the bar as Save, Cancel, a divider and the message, and Delete at the far right', async ({ page }) => {
  const { nameInput } = tagPage(page);
  await page.goto('/admin/tags/1002');
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

test.describe('paging', () => {
  test.use({ layer: 'more-tags' });

  test('pages the list by the address, and a tag opened from a page returns to it', async ({ page, api }) => {
    const { animationsDone } = tagPage(page);
    const listed: unknown = await (await api.get('/tags')).json();
    const total = Array.isArray(listed) ? listed.length : 0;
    // More than two pages of ten, so the last one is a short page: the layer has some twenty tags, and the test says what it
    // needs of it and no more.
    expect(total).toBeGreaterThan(20);
    const pageSize = 10;
    const range = (from: number, to: number): string =>
      `${from.toString()}–${to.toString()} of ${total.toString()} tags`;
    const lastFrom = Math.floor((total - 1) / pageSize) * pageSize + 1;
    const summary = page.getByTestId('paging-summary');
    await page.goto('/admin/tags');
    await expect(summary).toHaveText(range(1, pageSize));
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
    await expect(summary).toHaveText(range(pageSize + 1, 2 * pageSize));
    await expect(page.getByRole('link', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page');
    await page.reload();
    await expect(summary).toHaveText(range(pageSize + 1, 2 * pageSize));

    // "+ Add tag" and a tag open beside the list at the same page, and closing returns to it.
    await expect(page.getByRole('link', { name: '+ Add tag' })).toHaveAttribute('href', '/admin/tags/+?page=2');
    const opened = page.getByTestId('tag-row').first().getByRole('link').first();
    const name = ((await opened.textContent()) ?? '').trim();
    await opened.click();
    await expect(page).toHaveURL(/\/admin\/tags\/\d+\?page=2$/);
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(page).toHaveURL(/\/admin\/tags\?page=2$/);
    await expect(summary).toHaveText(range(pageSize + 1, 2 * pageSize));

    // Another size keeps the first row showing on screen, and a page past the end is the last one.
    await page.getByLabel('Per page').selectOption('25');
    await expect(page).toHaveURL(/\/admin\/tags\?size=25$/);
    await expect(summary).toHaveText(range(1, Math.min(25, total)));
    await page.goto('/admin/tags?page=9');
    await expect(summary).toHaveText(range(lastFrom, total));
  });
});

test('has a section of the tag for what uses it, at an address of its own, that keeps the form while it is open', async ({
  page,
}) => {
  const { nameInput } = tagPage(page);
  await page.goto('/admin/tags/1002/used-by');
  await expect(page.getByRole('heading', { level: 1, name: 'Language' })).toBeVisible();
  const sections = page.getByRole('navigation', { name: 'Sections of the tag' });
  await expect(sections.getByRole('link', { name: 'Used by' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByText('Nothing uses this tag yet.')).toBeVisible();
  await expect(nameInput()).toBeHidden();

  // A refresh stays on the section, and the other one is a link back.
  await page.reload();
  await expect(page.getByText('Nothing uses this tag yet.')).toBeVisible();
  await sections.getByRole('link', { name: 'Details' }).click();
  await expect(page).toHaveURL(/\/admin\/tags\/1002$/);
  await expect(nameInput()).toBeVisible();

  // Typing, and moving between the sections, is not leaving: no question, and what was typed is still there.
  await nameInput().fill('Language!');
  await sections.getByRole('link', { name: 'Used by' }).click();
  await expect(page).toHaveURL(/\/admin\/tags\/1002\/used-by$/);
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

test('draws the page in the mode it is in, with thin scrollbars that keep their room, and bars that the scrollbar stops above', async ({
  page,
}) => {
  const { animationsDone } = tagPage(page);
  await page.goto('/admin/tags/1002');
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

test('puts the tag in the breadcrumbs, with the list one step back at the page it was on', async ({ page }) => {
  const { nameInput } = tagPage(page);
  // The links are the steps back: the page you are on is marked as a link too, but is not one.
  const crumbs = page.getByRole('navigation', { name: 'breadcrumb' });

  await page.goto('/admin/tags');
  await expect(crumbs.locator('a')).toHaveText(['Admin']);
  await expect(crumbs.getByText('Tags', { exact: true })).toHaveAttribute('aria-current', 'page');

  await page.goto('/admin/tags/1002?page=1&size=25');
  await expect(crumbs.locator('a')).toHaveText(['Admin', 'Tags']);
  await expect(crumbs.locator('a', { hasText: 'Tags' })).toHaveAttribute('href', '/admin/tags?size=25');
  await expect(crumbs.getByText('Language', { exact: true })).toHaveAttribute('aria-current', 'page');

  // The section of the tag is still the tag, and a new tag is "New tag".
  await page.goto('/admin/tags/1002/used-by');
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

test('has the color of the mode in the page before any style has arrived, so a refresh does not flash white', async ({
  page,
  web,
}) => {
  // The page as the server sends it, before any stylesheet or script: a page drawn in the browser has no styles until its
  // script has run, and this is all that holds the color until then.
  const html = await (await web.get('/admin/tags/1002/used-by')).text();
  expect(html).toContain('<meta name="color-scheme" content="light dark" />');
  expect(html).toMatch(/html\s*\{\s*background:\s*oklch\(1 0 0\)/);
  expect(html).toMatch(/html\.dark\s*\{\s*background:\s*oklch\(0\.145 0 0\)/);

  // And the styles give each mode a color of its own, which the page follows as the system changes.
  await page.goto('/admin/tags/1002/used-by');
  const background = () => page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  await page.emulateMedia({ colorScheme: 'light' });
  await expect.poll(background).toBe('oklch(1 0 0)');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect.poll(background).not.toBe('oklch(1 0 0)');
  await page.emulateMedia({ colorScheme: 'light' });
});
