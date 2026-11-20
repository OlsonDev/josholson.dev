import { describe, expect, test } from 'bun:test';
import { adminPages, isCurrent, pageAt, passkeysPage } from './navigation.ts';

const page = (title: string) => {
  const found = [...adminPages, passkeysPage].find((candidate) => candidate.title === title);
  if (found === undefined) throw new Error(`no page named ${title}`);
  return found;
};

describe('isCurrent', () => {
  test('the dashboard is only itself, and not every page under /admin', () => {
    expect(isCurrent(page('Dashboard'), '/admin')).toBe(true);
    expect(isCurrent(page('Dashboard'), '/admin/tags')).toBe(false);
  });

  test('a page is itself and what is under it, and not a page whose name starts the same', () => {
    expect(isCurrent(page('Resumes'), '/admin/resumes')).toBe(true);
    expect(isCurrent(page('Resumes'), '/admin/resumes/foo/work')).toBe(true);
    expect(isCurrent(page('Resumes'), '/admin/resumes-old')).toBe(false);
  });
});

describe('pageAt', () => {
  test('finds the page, the passkeys page included, and is undefined elsewhere', () => {
    expect(pageAt('/admin/tags')?.title).toBe('Tags');
    expect(pageAt('/admin/passkeys')?.title).toBe('Passkeys');
    expect(pageAt('/admin/sign-in')).toBeUndefined();
  });

  test('every page has its own address', () => {
    const hrefs = adminPages.map((candidate) => candidate.href);

    expect(new Set(hrefs).size).toBe(hrefs.length);
  });
});
