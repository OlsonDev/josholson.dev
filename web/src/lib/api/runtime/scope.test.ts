import { describe, expect, test } from 'bun:test';

import { forwardScope, scopeHeader } from './scope.ts';

const apiUrl = 'http://localhost:5100';

function call(url: string, headers?: Record<string, string>): Request {
  return new Request(url, { headers });
}

describe('forwardScope', () => {
  test("puts the page request's scope on a call to the API", () => {
    const request = call(`${apiUrl}/tags`);

    forwardScope(request, new Headers({ [scopeHeader]: 'foo42' }), apiUrl, true);

    expect(request.headers.get(scopeHeader)).toBe('foo42');
  });

  test('does nothing unless the server was started for tests', () => {
    const request = call(`${apiUrl}/tags`);

    forwardScope(request, new Headers({ [scopeHeader]: 'foo42' }), apiUrl, false);

    expect(request.headers.has(scopeHeader)).toBe(false);
  });

  test('does nothing for a page request with no scope', () => {
    const request = call(`${apiUrl}/tags`);

    forwardScope(request, new Headers(), apiUrl, true);

    expect(request.headers.has(scopeHeader)).toBe(false);
  });

  test('leaves a call that has a scope of its own', () => {
    const request = call(`${apiUrl}/tags`, { [scopeHeader]: 'bar' });

    forwardScope(request, new Headers({ [scopeHeader]: 'foo42' }), apiUrl, true);

    expect(request.headers.get(scopeHeader)).toBe('bar');
  });

  test.each([
    ['another host', 'https://example.com/tags'],
    ['another port', 'http://localhost:51000/tags'],
    ['another scheme', 'https://localhost:5100/tags'],
    ['a host that only starts with the API', 'http://localhost.example.com:5100/tags'],
  ])('does not send the scope to %s', (_name, url) => {
    const request = call(url);

    forwardScope(request, new Headers({ [scopeHeader]: 'foo42' }), apiUrl, true);

    expect(request.headers.has(scopeHeader)).toBe(false);
  });
});
