import { afterEach, describe, expect, test } from 'bun:test';

import { ApiError } from './api-error.ts';
import * as http from './http.ts';
import { type CallOptions, configureApi, nullOnApiError, resetApi } from './request.ts';
import { arrayOf, instant, objectOf } from './revive.ts';

type Sent = { url: string; init: RequestInit };

type Fake = { fetch: typeof globalThis.fetch; sent: Sent[] };

// A `fetch` that records what it was asked and answers with `respond`. It rejects when its request is aborted.
function fakeFetch(respond: (sent: Sent) => Response | Promise<Response>): Fake {
  const sent: Sent[] = [];
  const send = async (url: string | URL | Request, init?: RequestInit): Promise<Response> => {
    const request = { url: typeof url === 'string' ? url : url instanceof URL ? url.href : url.url, init: init ?? {} };
    sent.push(request);
    const answer = Promise.resolve(respond(request));
    const signal = request.init.signal;
    if (signal === undefined || signal === null) return await answer;
    return await new Promise<Response>((resolve, reject) => {
      const abort = (): void => {
        reject(signal.reason);
      };
      if (signal.aborted) abort();
      signal.addEventListener('abort', abort, { once: true });
      answer.then(resolve, reject);
    });
  };
  return { fetch: Object.assign(send, { preconnect: () => undefined }), sent };
}

const json = (value: unknown, status = 200, contentType = 'application/json'): Response =>
  new Response(JSON.stringify(value), { status, headers: { 'content-type': contentType } });

// Holds requests in flight until `open` answers every one that is waiting (each gets its own copy of the response).
function gate(): { open: (response: Response) => void; wait: () => Promise<Response> } {
  const waiting: ((response: Response) => void)[] = [];
  return {
    wait: () =>
      new Promise<Response>((resolve) => {
        waiting.push(resolve);
      }),
    open: (response) => {
      for (const resolve of waiting) resolve(response.clone());
    },
  };
}

function firstSent(fake: Fake): Sent {
  const [sent] = fake.sent;
  if (sent === undefined) throw new Error('nothing was sent');
  return sent;
}

function signalOf(sent: Sent): AbortSignal {
  const { signal } = sent.init;
  if (signal === undefined || signal === null) throw new Error('the request had no signal');
  return signal;
}

function apiErrorOf(error: unknown): ApiError {
  if (!(error instanceof ApiError)) throw new Error(`not an ApiError: ${typeof error}`);
  return error;
}

function abortOf(error: unknown): DOMException {
  if (!(error instanceof DOMException)) throw new Error(`not a DOMException: ${typeof error}`);
  return error;
}

// A `GET` of `/foos` (or of `uri`), and a `POST` of a body to `/foos/search`, both answered with JSON.
function get<TResult = unknown>(
  options?: CallOptions,
  query?: Record<string, unknown>,
  uri = '/foos',
  name = 'foo.get',
): Promise<TResult> {
  return http.get<TResult>(options, name, uri, undefined, query);
}

function search(body: unknown, options?: CallOptions): Promise<unknown> {
  return http.post(options, 'foo.search', '/foos/search', body);
}

function use(fake: Fake, extra: Parameters<typeof configureApi>[0] = {}): void {
  configureApi({ baseUrl: 'https://api.test', fetch: () => fake.fetch, ...extra });
}

afterEach(() => {
  resetApi();
});

describe('building the request', () => {
  test('puts the query after the route, and sends to the base URL', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await get(undefined, { page: 2, q: 'x&y' }, '/foos/7/bars');

    expect(firstSent(fake).url).toBe('https://api.test/foos/7/bars?page=2&q=x%26y');
  });

  test('rejects, and sends nothing, when the client was never given a base URL', async () => {
    const fake = fakeFetch(() => json({}));
    configureApi({ fetch: () => fake.fetch });

    await expect(get()).rejects.toThrow('before the API client was configured');
    expect(fake.sent).toHaveLength(0);
  });

  test('an empty base URL is allowed, and sends to the same origin', async () => {
    const fake = fakeFetch(() => json({}));
    configureApi({ baseUrl: '', fetch: () => fake.fetch });

    await get();

    expect(firstSent(fake).url).toBe('/foos');
  });

  test('sends the method, JSON, and credentials, and an instant in the body as ISO text', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await search({ when: Temporal.Instant.from('2026-01-02T03:04:05Z'), n: 1 });

    const { init } = firstSent(fake);
    const headers = new Headers(init.headers);
    expect(init.method).toBe('POST');
    expect(init.credentials).toBe('include');
    expect(init.body).toBe('{"when":"2026-01-02T03:04:05Z","n":1}');
    expect(headers.get('accept')).toBe('application/json');
    expect(headers.get('content-type')).toBe('application/json');
  });

  test('each method sends its own, and a body goes before the shape and the query', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await http.get(undefined, 'foo.get', '/foos');
    await http.delete(undefined, 'foo.delete', '/foos/1');
    await http.put(undefined, 'foo.put', '/foos/1', { a: 1 }, undefined, { page: 2 });
    await http.patch(undefined, 'foo.patch', '/foos/1', { a: 1 });
    await http.post(undefined, 'foo.post', '/foos');

    expect(fake.sent.map((sent) => sent.init.method)).toEqual(['GET', 'DELETE', 'PUT', 'PATCH', 'POST']);
    expect(fake.sent[2]?.url).toBe('https://api.test/foos/1?page=2');
    expect(fake.sent[2]?.init.body).toBe('{"a":1}');
    expect(fake.sent[4]?.init.body).toBeUndefined();
  });

  test('sends no Content-Type without a body, and lets a call add headers', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await get({ headers: { 'X-Foo': 'bar' } });

    const headers = new Headers(firstSent(fake).init.headers);
    expect(headers.get('x-foo')).toBe('bar');
    expect(headers.has('content-type')).toBe(false);
  });
});

describe('the reply', () => {
  test('json is parsed, and its instants become Temporal.Instant', async () => {
    const fake = fakeFetch(() => json([{ id: 1, createdUtc: '2026-01-02T03:04:05Z' }]));
    use(fake);

    const result = await http.get(
      undefined,
      'foo.get',
      '/foos',
      arrayOf(objectOf<{ id: number; createdUtc: Temporal.Instant }>({ createdUtc: instant })),
    );

    expect(result[0]?.id).toBe(1);
    expect(result[0]?.createdUtc.toString()).toBe('2026-01-02T03:04:05Z');
  });

  test('a 201 is the Location, whatever else it says', async () => {
    const fake = fakeFetch(() => new Response(null, { status: 201, headers: { location: 'https://api.test/foos/9' } }));
    use(fake);

    const result = await http.post(undefined, 'foo.create', '/foos', {});

    expect(result).toEqual({ location: 'https://api.test/foos/9' });
  });

  test('a success with nothing in it is undefined, whatever its status', async () => {
    const fake = fakeFetch(() => new Response(null, { status: 204 }));
    use(fake);

    const result = await http.delete(undefined, 'foo.delete', '/foos/1');

    expect(result).toBeUndefined();
  });
});

describe('errors', () => {
  test('an error status throws an ApiError with the problem details, field errors, and trace ID', async () => {
    const problem = {
      title: 'One or more validation errors occurred.',
      status: 422,
      errors: { slug: ['Not kebab-case.'] },
      traceId: 't-1',
    };
    use(fakeFetch(() => json(problem, 422, 'application/problem+json')));

    const error = apiErrorOf(await search({}).catch((caught: unknown) => caught));

    expect(error.status).toBe(422);
    expect(error.is(422)).toBe(true);
    expect(error.operation).toBe('foo.search');
    expect(error.problem?.title).toBe('One or more validation errors occurred.');
    expect(error.fieldErrors).toEqual({ slug: ['Not kebab-case.'] });
    expect(error.traceId).toBe('t-1');
    expect(error.message).toBe('foo.search got 422: One or more validation errors occurred.');
  });

  test('an error whose body is not problem details still throws, with no problem', async () => {
    use(
      fakeFetch(
        () => new Response('<html>Bad gateway</html>', { status: 502, headers: { 'content-type': 'text/html' } }),
      ),
    );

    const error = apiErrorOf(await get().catch((caught: unknown) => caught));

    expect(error.status).toBe(502);
    expect(error.problem).toBeUndefined();
    expect(error.fieldErrors).toEqual({});
  });

  test('a network failure is an ApiError with status 0 and the cause', async () => {
    const cause = new TypeError('fetch failed');
    use(fakeFetch(() => Promise.reject(cause)));

    const error = apiErrorOf(await get().catch((caught: unknown) => caught));

    expect(error.status).toBe(0);
    expect(error.cause).toBe(cause);
    expect(error.message).toBe('foo.get got no response');
  });

  test('onError sees each ApiError before it is thrown, unless the call opts out', async () => {
    const seen: ApiError[] = [];
    use(
      fakeFetch(() => json({ title: 'Not Found' }, 404, 'application/problem+json')),
      {
        onError: (error) => {
          seen.push(error);
        },
      },
    );

    await expect(get()).rejects.toBeInstanceOf(ApiError);
    await expect(get({ onError: false })).rejects.toBeInstanceOf(ApiError);

    expect(seen).toHaveLength(1);
    expect(seen[0]?.status).toBe(404);
  });

  test('a handler in the options takes the error: its result is the call result, and the global hook does not see it', async () => {
    const seen: ApiError[] = [];
    const handled: ApiError[] = [];
    use(
      fakeFetch(() => json({ title: 'Not Found' }, 404, 'application/problem+json')),
      {
        onError: (error) => {
          seen.push(error);
        },
      },
    );

    const result = await get({
      onError: (error) => {
        handled.push(error);
        return 'fallback';
      },
    });

    expect(result).toBe('fallback');
    expect(handled).toHaveLength(1);
    expect(handled[0]?.status).toBe(404);
    expect(seen).toHaveLength(0);
  });

  test('a handler can throw to let the error go', async () => {
    use(fakeFetch(() => json({ title: 'Boom' }, 500, 'application/problem+json')));

    const error = apiErrorOf(
      await get({
        onError: (caught) => {
          if (caught.status === 404) return null;
          throw caught;
        },
      }).catch((caught: unknown) => caught),
    );

    expect(error.status).toBe(500);
  });

  test('nullOnApiError is null for an error status and for a network failure, and a result otherwise', async () => {
    use(fakeFetch(() => json({ title: 'Boom' }, 500, 'application/problem+json')));
    expect(await get(nullOnApiError)).toBeNull();

    use(fakeFetch(() => Promise.reject(new TypeError('fetch failed'))));
    expect(await get(nullOnApiError)).toBeNull();

    use(fakeFetch(() => json({ ok: true })));
    expect(await get(nullOnApiError)).toEqual({ ok: true });
  });

  test('nullOnApiError is frozen, and spreads with other options', async () => {
    use(fakeFetch(() => json({ title: 'Boom' }, 500, 'application/problem+json')));

    expect(Object.isFrozen(nullOnApiError)).toBe(true);
    expect(await get({ ...nullOnApiError, dedupe: false })).toBeNull();
  });

  test('a handler does not see an abort', async () => {
    const held = gate();
    use(fakeFetch(() => held.wait()));
    const controller = new AbortController();

    const pending = get({ ...nullOnApiError, signal: controller.signal });
    controller.abort();

    expect(abortOf(await pending.catch((caught: unknown) => caught)).name).toBe('AbortError');
  });

  test('an aborted call rejects with the signal reason, and is not an ApiError or an onError', async () => {
    const seen: ApiError[] = [];
    const held = gate();
    use(
      fakeFetch(() => held.wait()),
      {
        onError: (error) => {
          seen.push(error);
        },
      },
    );
    const controller = new AbortController();

    const pending = get({ signal: controller.signal });
    controller.abort();

    expect(abortOf(await pending.catch((caught: unknown) => caught)).name).toBe('AbortError');
    expect(seen).toHaveLength(0);
  });
});

describe('sharing a request that is in flight', () => {
  test('identical calls at once send one request, and each gets a result of its own', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const first = get<{ items: number[] }>();
    const second = get<{ items: number[] }>();
    held.open(json({ items: [1, 2] }));
    const [a, b] = await Promise.all([first, second]);

    expect(fake.sent).toHaveLength(1);
    expect(a).toEqual({ items: [1, 2] });
    a.items.push(3);
    expect(b).toEqual({ items: [1, 2] });
  });

  test('a POST is shared too, when its body is the same', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const first = search({ filter: { a: 1, b: 2 }, page: 1 });
    const second = search({ filter: { a: 1, b: 2 }, page: 1 });
    held.open(json([]));
    await Promise.all([first, second]);

    expect(fake.sent).toHaveLength(1);
  });

  test('a body changed after its call started does not change what it is compared with', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const body = { page: 1 };
    const first = search(body);
    body.page = 2;
    const second = search({ page: 2 });
    held.open(json([]));
    await Promise.all([first, second]);

    expect(fake.sent).toHaveLength(2);
  });

  test('the same members in another order are another request, which costs a request and nothing else', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [search({ page: 1, size: 5 }), search({ size: 5, page: 1 })];
    held.open(json([]));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(2);
  });

  test('another operation with the same URL and body is another request', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [get(undefined, undefined, '/foos', 'foo.one'), get(undefined, undefined, '/foos', 'foo.two')];
    held.open(json({}));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(2);
  });

  test('calls that differ in body, URL, or a header are different requests', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [
      search({ page: 1 }),
      search({ page: 2 }),
      get(undefined, { page: 1 }),
      get(undefined, { page: 2 }),
      get({ headers: { 'X-Foo': 'bar' } }, { page: 2 }),
    ];
    held.open(json({}));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(5);
  });

  test('dedupe: false sends its own request, and is not shared', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [get(), get({ dedupe: false }), get()];
    held.open(json({}));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(2);
  });

  test('only while in flight: a call after the first one is done sends again', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await get();
    await get();

    expect(fake.sent).toHaveLength(2);
  });

  test('a different fetch is a different set of requests', async () => {
    const held = gate();
    const one = fakeFetch(() => held.wait());
    const other = fakeFetch(() => held.wait());

    use(one);
    const first = get();
    use(other);
    const second = get();
    held.open(json({}));
    await Promise.all([first, second]);

    expect(one.sent).toHaveLength(1);
    expect(other.sent).toHaveLength(1);
  });

  test('a failure reaches every caller, and onError sees it once for each', async () => {
    const held = gate();
    const seen: ApiError[] = [];
    use(
      fakeFetch(() => held.wait()),
      {
        onError: (error) => {
          seen.push(error);
        },
      },
    );

    const first = get().catch((caught: unknown) => caught);
    const second = get().catch((caught: unknown) => caught);
    held.open(json({ title: 'Boom' }, 500, 'application/problem+json'));
    const [a, b] = await Promise.all([first, second]);

    expect(apiErrorOf(a).status).toBe(500);
    expect(apiErrorOf(b).status).toBe(500);
    expect(seen).toHaveLength(2);
  });

  test('one caller aborting leaves the request and the other caller alone', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);
    const controller = new AbortController();

    const aborts = get({ signal: controller.signal }).catch((caught: unknown) => caught);
    const stays = get<{ ok: boolean }>();
    controller.abort();
    held.open(json({ ok: true }));

    expect(abortOf(await aborts).name).toBe('AbortError');
    expect(await stays).toEqual({ ok: true });
    expect(fake.sent).toHaveLength(1);
    expect(signalOf(firstSent(fake)).aborted).toBe(false);
  });

  test('the request stops when every caller has aborted', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);
    const one = new AbortController();
    const two = new AbortController();

    const first = get({ signal: one.signal }).catch((caught: unknown) => caught);
    const second = get({ signal: two.signal }).catch((caught: unknown) => caught);
    one.abort();
    expect(signalOf(firstSent(fake)).aborted).toBe(false);
    two.abort();

    expect(abortOf(await first).name).toBe('AbortError');
    expect(abortOf(await second).name).toBe('AbortError');
    expect(fake.sent).toHaveLength(1);
    expect(signalOf(firstSent(fake)).aborted).toBe(true);
  });

  test('onDuplicate sees a call that joined, with both call stacks', async () => {
    const held = gate();
    const duplicates: { operation: string; url: string; first: string; second: string }[] = [];
    use(
      fakeFetch(() => held.wait()),
      {
        onDuplicate: (duplicate) => {
          duplicates.push(duplicate);
        },
      },
    );

    const first = get();
    const second = get();
    held.open(json({}));
    await Promise.all([first, second]);

    expect(duplicates).toHaveLength(1);
    expect(duplicates[0]?.operation).toBe('foo.get');
    expect(duplicates[0]?.url).toBe('https://api.test/foos');
    expect(duplicates[0]?.first).toContain('request.test.ts');
    expect(duplicates[0]?.second).toContain('request.test.ts');
  });
});

test('the fetch is asked for when the call starts, so a request-scoped one is the one used', async () => {
  const early = fakeFetch(() => json({}));
  const late = fakeFetch(() => json({}));
  let current = early;
  configureApi({ baseUrl: 'https://api.test', fetch: () => current.fetch });

  const first = get();
  current = late;
  await first;
  await get();

  expect(early.sent).toHaveLength(1);
  expect(late.sent).toHaveLength(1);
});
