import { expect, test } from './support/scope.ts';

test('the homepage renders with or without the API', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Josh Olson' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'Tags' })).toBeVisible();
});
