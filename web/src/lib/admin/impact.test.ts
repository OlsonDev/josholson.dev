import { describe, expect, test } from 'bun:test';

import { ApiError } from '#lib/api/index.ts';
import type { Problem } from '#lib/api/runtime/api-error.ts';
import { DeletionImpactKind } from '#lib/api/schema.ts';

import { describeImpactItem } from './impact-kinds.ts';
import { impactFromError, isBlocked, isDeletionImpact } from './impact.ts';

const impact = { blockers: [{ kind: DeletionImpactKind.Skill, count: 2 }], warnings: [] };

// What the client holds as an error's body is JSON the API sent, whatever its shape.
function body(value: unknown): Problem {
  return JSON.parse(JSON.stringify(value));
}

describe('isDeletionImpact', () => {
  test('accepts what the API sends and nothing else', () => {
    expect(isDeletionImpact(impact)).toBe(true);
    expect(isDeletionImpact({ blockers: [] })).toBe(false);
    expect(isDeletionImpact({ title: 'Conflict' })).toBe(false);
    expect(isDeletionImpact(null)).toBe(false);
    expect(isDeletionImpact('foo')).toBe(false);
  });
});

describe('impactFromError', () => {
  test('reads the blockers from a 409', () => {
    const error = new ApiError('tag.delete', 409, body(impact));
    expect(impactFromError(error)).toEqual(impact);
  });

  test('is null for another status, another body, or another kind of error', () => {
    expect(impactFromError(new ApiError('tag.delete', 404, body(impact)))).toBeNull();
    expect(impactFromError(new ApiError('tag.delete', 409, { title: 'Conflict' }))).toBeNull();
    expect(impactFromError(new Error('foo'))).toBeNull();
  });
});

describe('isBlocked', () => {
  test('is true only when something blocks', () => {
    expect(isBlocked(impact)).toBe(true);
    expect(isBlocked({ blockers: [], warnings: impact.blockers })).toBe(false);
  });
});

describe('describeImpactItem', () => {
  test('counts with the singular or the plural', () => {
    expect(describeImpactItem({ kind: DeletionImpactKind.Skill, count: 1 })).toBe('1 skill');
    expect(describeImpactItem({ kind: DeletionImpactKind.Project, count: 3 })).toBe('3 projects');
    expect(describeImpactItem({ kind: DeletionImpactKind.Post, count: 2 })).toBe('2 blog posts');
  });
});
