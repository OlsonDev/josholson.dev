import type { Page } from '@playwright/test';

// What the tests of the admin's tag pages share (the `some-tags` layer is the account's child, holding Database, Language,
// and Tool: 1001 to 1003, so Language is 1002, which the tests that open it by its address name, and the heading they wait
// for is what notices a layer that has moved).

/** `rgb(r, g, b)` of a `#rrggbb`, the way the browser reports a computed color. */
export const rgbOf = (hex: string): string =>
  `rgb(${[1, 3, 5].map((start) => Number.parseInt(hex.slice(start, start + 2), 16)).join(', ')})`;

/** What a test of the tags pages finds on the page, for the `page` it is given. */
export function tagPage(page: Page) {
  return {
    // In the table: a tag's name is in the breadcrumbs too, as the page you are on.
    tagLink: (name: string) => page.getByRole('table').getByRole('link', { name, exact: true }),
    saveButton: () => page.getByRole('button', { name: 'Save tag', exact: true }),
    nameInput: () => page.getByLabel('Name', { exact: true }),
    slugInput: () => page.getByLabel('Slug', { exact: true }),
    // Waits until what is animating (a popover zooming in, say) has finished, or been canceled by the next thing.
    animationsDone: () =>
      page.evaluate(() =>
        Promise.all(document.getAnimations().map((animation) => animation.finished.catch(() => undefined))),
      ),
    // Where each field is on the page, from the top, which should not change when a field is focused.
    fieldTops: () =>
      page.evaluate(() =>
        ['name', 'slug', 'color', 'description'].map(
          (field) => document.querySelector(`[data-field="${field}"]`)?.getBoundingClientRect().top,
        ),
      ),
    // Where the help text under each input is, from the top: the distance from an input to the text under it is what a
    // password manager's icon can change, by adding an element beside the input.
    helpTops: () =>
      page.evaluate(() =>
        ['name', 'slug'].map((field) => {
          const holder = document.querySelector(`[data-field="${field}"]`)!;
          return [
            holder.querySelector('input')!.getBoundingClientRect().height,
            holder.querySelector('p')!.getBoundingClientRect().top -
              holder.querySelector('input')!.getBoundingClientRect().bottom,
          ];
        }),
      ),
  };
}
