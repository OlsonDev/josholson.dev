import { describe, expect, test } from 'bun:test';

import { listHref, paneHref } from './routes.ts';

describe('the tags addresses', () => {
  test('the list and a pane carry the list page along', () => {
    expect(listHref()).toBe('/admin/tags');
    expect(listHref('?page=2')).toBe('/admin/tags?page=2');
    expect(paneHref(5)).toBe('/admin/tags/5');
    expect(paneHref(5, '?page=2&size=25')).toBe('/admin/tags/5?page=2&size=25');
    expect(paneHref('+', '?page=3')).toBe('/admin/tags/+?page=3');
  });

  test('a section is under the tag', () => {
    expect(paneHref(5, '', 'used-by')).toBe('/admin/tags/5/used-by');
    expect(paneHref(5, '?page=2', 'used-by')).toBe('/admin/tags/5/used-by?page=2');
  });
});
