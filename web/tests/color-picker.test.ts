import { tagPage } from './support/admin.ts';
import { expect, test } from './support/scope.ts';

// The color picker of the tag form, which opens on a new tag. Each test has the account signed in on the `some-tags` layer
// (ADR 0033, a scoped database per test), and none depends on another.

test.use({ layer: 'some-tags', signedIn: true });

test('rings the swatch you are on, the same for the pointer and the keys, and marks the chosen color with a check', async ({
  page,
}) => {
  const { animationsDone } = tagPage(page);
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

test('lets the pointer take over from the keys only once it crosses into another swatch', async ({ page }) => {
  const { animationsDone } = tagPage(page);
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

test('moves through the palette with the arrow keys, and a sideways move into black or white undoes itself', async ({
  page,
}) => {
  const { animationsDone } = tagPage(page);
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

test('starts the keys at the choice, and a required color has no "No color" to go to', async ({ page }) => {
  const { animationsDone } = tagPage(page);
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

test('shows an unchosen color dimmed with no swatch, and a chosen one with its swatch and name', async ({ page }) => {
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

test('keeps the swatch it came from while the pointer crosses the gaps between swatches', async ({ page }) => {
  const { animationsDone } = tagPage(page);
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

test('puts black and white in one column at the right, three rows each, whichever is at the top of the scale on top', async ({
  page,
}) => {
  const { animationsDone } = tagPage(page);
  await page.goto('/admin/tags/+');
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

test('keeps the contrast panel the same size as the pointer moves from color to color', async ({ page }) => {
  await page.goto('/admin/tags/+');
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

test('spaces the parts of the color picker evenly, from the top edge to the bottom one', async ({ page }) => {
  const { animationsDone } = tagPage(page);
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
