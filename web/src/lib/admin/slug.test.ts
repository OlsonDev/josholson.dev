import { describe, expect, test } from 'bun:test';

import { isSlug, nextSlug, slugify } from './slug.ts';

describe('slugify', () => {
  test('lowercases and joins words with single hyphens', () => {
    expect(slugify('Programming Language')).toBe('programming-language');
    expect(slugify('  Foo   Bar  ')).toBe('foo-bar');
    expect(slugify('C# / .NET')).toBe('c-net');
  });

  test('keeps digits, drops accents and symbols, and may come out empty', () => {
    expect(slugify('Web 3.0')).toBe('web-3-0');
    expect(slugify('Café')).toBe('caf');
    expect(slugify('---')).toBe('');
    expect(slugify('')).toBe('');
  });

  test('always gives something isSlug accepts, unless empty', () => {
    for (const name of ['Foo', 'foo--bar', ' - x - ', 'A1 B2']) expect(isSlug(slugify(name))).toBe(true);
  });
});

describe('isSlug', () => {
  test('matches the domain', () => {
    expect(isSlug('foo')).toBe(true);
    expect(isSlug('foo-bar-42')).toBe(true);
    for (const bad of ['', 'Foo', 'foo--bar', '-foo', 'foo-', 'foo bar', 'foo_bar']) expect(isSlug(bad)).toBe(false);
  });
});

describe('nextSlug', () => {
  test('follows the name while the slug is what the old name suggested', () => {
    expect(nextSlug('', 'F', '')).toBe('f');
    expect(nextSlug('Foo', 'Foo Bar', 'foo')).toBe('foo-bar');
    expect(nextSlug('Foo Bar', 'Foo Ba', 'foo-bar')).toBe('foo-ba');
  });

  test('stays once the person has changed it', () => {
    expect(nextSlug('Foo', 'Foo Bar', 'baz')).toBe('baz');
    expect(nextSlug('Foo', 'Foo Bar', 'foo-')).toBe('foo-');
  });

  test('follows again when an edit of the name brings the two back together', () => {
    expect(nextSlug('Foo', 'Foo!', 'foo')).toBe('foo');
    expect(nextSlug('Foo Bar', '', 'foo-bar')).toBe('');
  });
});
