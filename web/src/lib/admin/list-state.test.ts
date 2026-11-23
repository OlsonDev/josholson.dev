import { describe, expect, test } from 'bun:test';

import { normalizeSearch, pagingSearch, parsePaging, visiblePages } from './list-state.ts';

const params = (query: string) => new URLSearchParams(query);

describe('parsePaging', () => {
  test('starts on the first page at the usual size', () => {
    expect(parsePaging(params(''), 22)).toEqual({ page: 1, size: 10, total: 22, pageCount: 3, start: 0, end: 10 });
  });

  test('reads a page and a size, and the last page has what is left', () => {
    expect(parsePaging(params('page=3'), 22)).toMatchObject({ page: 3, start: 20, end: 22 });
    expect(parsePaging(params('page=2&size=25'), 60)).toMatchObject({ page: 2, size: 25, start: 25, end: 50 });
  });

  test('a page past the end is the last, and what cannot be used is the default', () => {
    expect(parsePaging(params('page=9'), 22).page).toBe(3);
    for (const bad of ['page=0', 'page=-2', 'page=x', 'page=1.5']) expect(parsePaging(params(bad), 22).page).toBe(1);
    expect(parsePaging(params('size=7'), 22).size).toBe(10);
    expect(parsePaging(params('size=abc'), 22).size).toBe(10);
  });

  test('an empty list is one empty page', () => {
    expect(parsePaging(params('page=4'), 0)).toEqual({ page: 1, size: 10, total: 0, pageCount: 1, start: 0, end: 0 });
  });
});

describe('pagingSearch', () => {
  test('leaves out what is the default', () => {
    expect(pagingSearch({ page: 1, size: 10 })).toBe('');
    expect(pagingSearch({ page: 2, size: 10 })).toBe('?page=2');
    expect(pagingSearch({ page: 1, size: 25 })).toBe('?size=25');
    expect(pagingSearch({ page: 3, size: 50 })).toBe('?page=3&size=50');
  });

  test('is what parsePaging reads back', () => {
    const search = pagingSearch({ page: 2, size: 25 });
    expect(parsePaging(new URLSearchParams(search), 100)).toMatchObject({ page: 2, size: 25 });
  });
});

describe('visiblePages', () => {
  test('shows every page when they fit', () => {
    expect(visiblePages(1, 1)).toEqual([1]);
    expect(visiblePages(2, 3)).toEqual([1, 2, 3]);
    expect(visiblePages(4, 7)).toEqual([1, 2, 3, 4, 5, 6, 7]);
  });

  test('keeps the ends, and a run or the current page with its neighbors between them', () => {
    expect(visiblePages(1, 10)).toEqual([1, 2, 3, 4, 5, 'gap', 10]);
    expect(visiblePages(4, 10)).toEqual([1, 2, 3, 4, 5, 'gap', 10]);
    expect(visiblePages(5, 10)).toEqual([1, 'gap', 4, 5, 6, 'gap', 10]);
    expect(visiblePages(6, 10)).toEqual([1, 'gap', 5, 6, 7, 'gap', 10]);
    expect(visiblePages(7, 10)).toEqual([1, 'gap', 6, 7, 8, 9, 10]);
    expect(visiblePages(10, 10)).toEqual([1, 'gap', 6, 7, 8, 9, 10]);
  });

  test('is always the same number of places, whichever page is showing, so nothing moves', () => {
    for (const pageCount of [8, 9, 10, 25, 100, 1000]) {
      for (let page = 1; page <= pageCount; page++) {
        const shown = visiblePages(page, pageCount);
        expect(shown).toHaveLength(7);
        expect(shown[0]).toBe(1);
        expect(shown.at(-1)).toBe(pageCount);
        expect(shown).toContain(page);
      }
    }
  });
});

describe('normalizeSearch', () => {
  test('is the plain form of the paging, and nothing else', () => {
    expect(normalizeSearch(params(''))).toBe('');
    expect(normalizeSearch(params('page=1&size=25'))).toBe('?size=25');
    expect(normalizeSearch(params('page=3&size=10&x=1'))).toBe('?page=3');
    expect(normalizeSearch(params('page=abc&size=7'))).toBe('');
  });
});
