import { describe, expect, test } from 'bun:test';

import type { TagResponse } from '#lib/api/schema.ts';

import { toRequest, validateTag, valuesFromTag } from './tag-form.ts';

function tag(overrides: Partial<TagResponse> = {}): TagResponse {
  return {
    tagId: 1,
    name: 'Foo',
    slug: 'foo',
    description: null,
    descriptionHtml: null,
    color: null,
    version: 1,
    aliases: [],
    previousSlugs: [],
    ...overrides,
  };
}

const blank = valuesFromTag(null);
const valid = { ...blank, name: 'Bar', slug: 'bar', color: 'emerald-200' };

describe('valuesFromTag and toRequest', () => {
  test('a new tag is blank and an existing one is its fields', () => {
    expect(blank).toEqual({ name: '', slug: '', description: '', color: null, aliases: [], previousSlugs: [] });
    expect(valuesFromTag(tag({ description: 'x', color: 'red-200', aliases: ['a'], previousSlugs: ['old'] }))).toEqual({
      name: 'Foo',
      slug: 'foo',
      description: 'x',
      color: 'red-200',
      aliases: ['a'],
      previousSlugs: ['old'],
    });
  });

  test('the request trims, makes a blank description none, and drops blank aliases', () => {
    const request = toRequest(
      { ...valid, name: ' Bar ', description: '  ', aliases: [' b ', ' ', 'c'], previousSlugs: ['old'] },
      3,
    );
    expect(request).toEqual({
      name: 'Bar',
      slug: 'bar',
      description: null,
      color: 'emerald-200',
      aliases: ['b', 'c'],
      previousSlugs: ['old'],
      expectedVersion: 3,
    });
    expect(toRequest(valid, null).expectedVersion).toBeNull();
  });
});

describe('validateTag', () => {
  test('a good tag has no errors', () => {
    expect(validateTag(valid, null, [tag()])).toEqual({});
  });

  test('name and slug are required, and the slug has a shape', () => {
    expect(validateTag(blank, null, [])).toEqual({
      name: ['Name is required.'],
      slug: ['Slug is required.'],
      color: ['Color is required.'],
    });
    expect(validateTag({ ...valid, slug: 'Not Kebab' }, null, [])).toHaveProperty('slug');
    expect(validateTag({ ...valid, slug: 'foo--bar' }, null, [])).toHaveProperty('slug');
  });

  test('another tag has the name (ignoring case) or the slug, current or previous', () => {
    const others = [tag({ name: 'Baz', slug: 'baz', previousSlugs: ['old-baz'] })];
    expect(validateTag({ ...valid, name: 'BAZ' }, null, others)).toEqual({ name: ['Name is already in use.'] });
    expect(validateTag({ ...valid, slug: 'baz' }, null, others)).toEqual({ slug: ['Slug is already in use.'] });
    expect(validateTag({ ...valid, slug: 'old-baz' }, null, others)).toEqual({ slug: ['Slug is already in use.'] });
  });

  test('a tag does not collide with itself', () => {
    const own = tag({ tagId: 5, name: 'Bar', slug: 'bar', aliases: ['b'], previousSlugs: ['old'] });
    expect(validateTag({ ...valid, aliases: ['b'], previousSlugs: ['old'] }, 5, [own])).toEqual({});
    expect(validateTag({ ...valid, slug: 'old' }, 5, [own])).toEqual({});
  });

  test('an unknown color is refused', () => {
    expect(validateTag({ ...valid, color: 'emerald-150' }, null, [])).toEqual({
      color: ['Color must be a Tailwind color.'],
    });
    expect(validateTag({ ...valid, color: 'emerald-200' }, null, [])).toEqual({});
  });

  test('each alias is checked by its place in the list', () => {
    const others = [tag({ name: 'Baz', aliases: ['qux'] })];
    const errors = validateTag({ ...valid, aliases: ['ok', '', 'OK', 'bar', 'QUX', 'baz'] }, null, others);
    expect(Object.keys(errors).toSorted()).toEqual([
      'aliases[1]',
      'aliases[2]',
      'aliases[3]',
      'aliases[4]',
      'aliases[5]',
    ]);
    expect(errors['aliases[2]']).toEqual(['Alias is listed twice.']);
    expect(errors['aliases[3]']).toEqual(["Alias repeats the tag's name."]);
  });
});
