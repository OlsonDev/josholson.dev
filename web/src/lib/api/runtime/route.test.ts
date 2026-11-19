import { describe, expect, test } from 'bun:test';
import { route, withQuery } from './route.ts';

describe('route', () => {
  test('fills the values in, each encoded as one segment', () => {
    const fooId = 7;
    const name = 'a b/c';

    expect(route`/foos/${fooId}/bars/${name}`).toBe('/foos/7/bars/a%20b%2Fc');
  });

  test('is the route itself when there is nothing to fill in', () => {
    expect(route`/foos`).toBe('/foos');
  });

  test('takes a boolean, and an instant as ISO text', () => {
    const when = Temporal.Instant.from('2026-01-02T03:04:05Z');

    expect(route`/foos/${true}/at/${when}`).toBe('/foos/true/at/2026-01-02T03%3A04%3A05Z');
  });

  test('throws when a value is missing, and says which route', () => {
    // What a caller in JavaScript, or a cast, could do: the tag with nothing where a value goes.
    const parts = Object.assign(['/foos/', '/bars'], { raw: ['/foos/', '/bars'] });

    expect(() => Reflect.apply(route, undefined, [parts, undefined])).toThrow(
      'A path parameter has no value: /foos/{}/bars',
    );
  });
});

describe('withQuery', () => {
  test('is the route when there is no query, or nothing in it', () => {
    expect(withQuery('/foos', undefined)).toBe('/foos');
    expect(withQuery('/foos', { skip: undefined, none: null })).toBe('/foos');
  });

  test('puts each parameter in the query string, encoded, and repeats a key for an array', () => {
    expect(withQuery('/foos', { page: 2, q: 'x&y', tag: ['a', 'b'], on: true })).toBe(
      '/foos?page=2&q=x%26y&tag=a&tag=b&on=true',
    );
  });

  test('sends an instant as ISO text', () => {
    expect(withQuery('/foos', { since: Temporal.Instant.from('2026-01-02T03:04:05Z') })).toBe(
      '/foos?since=2026-01-02T03%3A04%3A05Z',
    );
  });

  test('rejects a value that has no one text', () => {
    expect(() => withQuery('/foos', { filter: { a: 1 } })).toThrow('a string, a number, a boolean, or an instant');
  });
});
