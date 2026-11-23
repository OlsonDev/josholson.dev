import { describe, expect, test } from 'bun:test';

import { countMessages, firstInvalid, sameData, summarizeErrors, withoutFieldErrors } from './form-logic.ts';

describe('sameData', () => {
  test('compares primitives, arrays, and objects by what they hold', () => {
    expect(sameData('a', 'a')).toBe(true);
    expect(sameData(null, null)).toBe(true);
    expect(sameData({ a: [1, { b: 2 }] }, { a: [1, { b: 2 }] })).toBe(true);
    expect(sameData({ a: 1 }, { a: 2 })).toBe(false);
    expect(sameData({ a: 1 }, { a: 1, b: undefined })).toBe(false);
    expect(sameData([1, 2], [2, 1])).toBe(false);
    expect(sameData([], {})).toBe(false);
    expect(sameData(null, {})).toBe(false);
  });
});

describe('summarizeErrors', () => {
  test('says "the validation error" for one and counts several', () => {
    expect(summarizeErrors(1)).toBe('Please address the validation error.');
    expect(summarizeErrors(2)).toBe('Please address the 2 validation errors.');
    expect(summarizeErrors(12)).toBe('Please address the 12 validation errors.');
  });
});

describe('countMessages', () => {
  test('counts every message of every field', () => {
    expect(countMessages({})).toBe(0);
    expect(countMessages({ name: ['a'], slug: ['b', 'c'], color: [] })).toBe(3);
  });
});

describe('firstInvalid', () => {
  test('is the topmost field on the page that has an error, whatever order the errors came in', () => {
    const fields = ['name', 'slug', 'description', 'aliases[0]'];
    expect(firstInvalid(fields, { 'aliases[0]': ['x'], slug: ['y'] })).toBe('slug');
    expect(firstInvalid(fields, { slug: [] })).toBeUndefined();
    expect(firstInvalid(fields, { unknown: ['z'] })).toBeUndefined();
  });
});

describe('withoutFieldErrors', () => {
  test('drops the field and the entries under it, and nothing else', () => {
    const errors = { aliases: ['a'], 'aliases[1]': ['b'], aliasesOther: ['c'], name: ['d'] };
    expect(withoutFieldErrors(errors, 'aliases')).toEqual({ aliasesOther: ['c'], name: ['d'] });
  });

  test('returns the same object when there is nothing to drop', () => {
    const errors = { name: ['d'] };
    expect(withoutFieldErrors(errors, 'slug')).toBe(errors);
  });
});
