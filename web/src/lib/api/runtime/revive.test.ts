import { expect, test } from 'bun:test';

import { arrayOf, instant, objectOf, revive } from './revive.ts';

function instantOf(value: unknown): Temporal.Instant {
  if (!(value instanceof Temporal.Instant)) throw new Error(`not an instant: ${typeof value}`);
  return value;
}

function recordOf(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null) throw new Error('not an object');
  return Object.fromEntries(Object.entries(value));
}

function listOf(value: unknown): unknown[] {
  if (!Array.isArray(value)) throw new Error('not an array');
  return value;
}

test('revive turns an instant string into a Temporal.Instant, to the nanosecond', () => {
  const value = instantOf(revive('2026-11-16T12:30:00.123456789Z', instant));

  expect(value.toString()).toBe('2026-11-16T12:30:00.123456789Z');
});

test('revive leaves null and undefined alone', () => {
  expect(revive(null, instant)).toBeNull();
  expect(revive(undefined, instant)).toBeUndefined();
  expect(revive({ when: null }, objectOf({ when: instant }))).toEqual({ when: null });
});

test('revive converts the named members of an object and leaves the rest', () => {
  const value = recordOf(
    revive({ id: 7, name: 'foo', createdUtc: '2026-01-02T03:04:05Z' }, objectOf({ createdUtc: instant })),
  );

  expect(value['id']).toBe(7);
  expect(value['name']).toBe('foo');
  expect(instantOf(value['createdUtc']).toString()).toBe('2026-01-02T03:04:05Z');
});

test('revive converts every item of an array', () => {
  const items = listOf(
    revive([{ when: '2026-01-02T03:04:05Z' }, { when: '2027-01-02T03:04:05Z' }], arrayOf(objectOf({ when: instant }))),
  );

  expect(items).toHaveLength(2);
  expect(items.map((item) => instantOf(recordOf(item)['when']).toString())).toEqual([
    '2026-01-02T03:04:05Z',
    '2027-01-02T03:04:05Z',
  ]);
});

test('revive goes into nested objects and arrays', () => {
  const value = recordOf(
    revive({ items: [{ at: '2026-01-02T03:04:05Z' }] }, objectOf({ items: arrayOf(objectOf({ at: instant })) })),
  );

  const [item] = listOf(value['items']);
  expect(instantOf(recordOf(item)['at']).toString()).toBe('2026-01-02T03:04:05Z');
});

test('revive ignores a member the response does not have, and a shape that does not fit the value', () => {
  expect(revive({ id: 1 }, objectOf({ createdUtc: instant }))).toEqual({ id: 1 });
  expect(revive('not an array', arrayOf(instant))).toBe('not an array');
  expect(revive(5, objectOf({ a: instant }))).toBe(5);
});
