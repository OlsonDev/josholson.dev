import { afterEach, describe, expect, test } from 'bun:test';
import { ApiError } from './api-error.ts';
import { call, configureApi, resetApi, type OperationSpec } from './request.ts';
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

const get = (path: string): OperationSpec => ({ name: 'foo.get', method: 'GET', path, reply: 'json' });
const post: OperationSpec = { name: 'foo.search', method: 'POST', path: '/foos/search', reply: 'json' };

function use(fake: Fake, extra: Parameters<typeof configureApi>[0] = {}): void {
  configureApi({ baseUrl: 'https://api.test', fetch: () => fake.fetch, ...extra });
}

afterEach(() => {
  resetApi();
});

describe('building the request', () => {
  test('fills path parameters, puts the rest in the query string, and encodes both', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await call(get('/foos/{fooId}/bars/{name}'), { fooId: 7, name: 'a b/c', page: 2, q: 'x&y' }, undefined, undefined);

    expect(firstSent(fake).url).toBe('https://api.test/foos/7/bars/a%20b%2Fc?page=2&q=x%26y');
  });

  test('repeats a key for an array and leaves out undefined and null', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await call(get('/foos'), { tag: ['a', 'b'], skip: undefined, none: null, on: true }, undefined, undefined);

    expect(firstSent(fake).url).toBe('https://api.test/foos?tag=a&tag=b&on=true');
  });

  test('sends an instant in the query string as ISO text', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await call(get('/foos'), { since: Temporal.Instant.from('2026-01-02T03:04:05Z') }, undefined, undefined);

    expect(firstSent(fake).url).toBe('https://api.test/foos?since=2026-01-02T03%3A04%3A05Z');
  });

  test('rejects, and sends nothing, when the client was never given a base URL', async () => {
    const fake = fakeFetch(() => json({}));
    configureApi({ fetch: () => fake.fetch });

    await expect(call(get('/foos'), undefined, undefined, undefined)).rejects.toThrow(
      'before the API client was configured',
    );
    expect(fake.sent).toHaveLength(0);
  });

  test('an empty base URL is allowed, and sends to the same origin', async () => {
    const fake = fakeFetch(() => json({}));
    configureApi({ baseUrl: '', fetch: () => fake.fetch });

    await call(get('/foos'), undefined, undefined, undefined);

    expect(firstSent(fake).url).toBe('/foos');
  });

  test('rejects when a path parameter has no value', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await expect(call(get('/foos/{fooId}'), {}, undefined, undefined)).rejects.toThrow('needs a value for fooId');
    expect(fake.sent).toHaveLength(0);
  });

  test('sends the method, JSON, and credentials, and an instant in the body as ISO text', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await call(post, undefined, { when: Temporal.Instant.from('2026-01-02T03:04:05Z'), n: 1 }, undefined);

    const { init } = firstSent(fake);
    const headers = new Headers(init.headers);
    expect(init.method).toBe('POST');
    expect(init.credentials).toBe('include');
    expect(init.body).toBe('{"when":"2026-01-02T03:04:05Z","n":1}');
    expect(headers.get('accept')).toBe('application/json');
    expect(headers.get('content-type')).toBe('application/json');
  });

  test('sends no Content-Type without a body, and lets a call add headers', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await call(get('/foos'), undefined, undefined, { headers: { 'X-Foo': 'bar' } });

    const headers = new Headers(firstSent(fake).init.headers);
    expect(headers.get('x-foo')).toBe('bar');
    expect(headers.has('content-type')).toBe(false);
  });
});

describe('the reply', () => {
  test('json is parsed, and its instants become Temporal.Instant', async () => {
    const fake = fakeFetch(() => json([{ id: 1, createdUtc: '2026-01-02T03:04:05Z' }]));
    use(fake);

    const result = await call<{ id: number; createdUtc: Temporal.Instant }[]>(
      { ...get('/foos'), revive: arrayOf(objectOf({ createdUtc: instant })) },
      undefined,
      undefined,
      undefined,
    );

    expect(result[0]?.id).toBe(1);
    expect(result[0]?.createdUtc.toString()).toBe('2026-01-02T03:04:05Z');
  });

  test('created returns the Location', async () => {
    const fake = fakeFetch(() => new Response(null, { status: 201, headers: { location: 'https://api.test/foos/9' } }));
    use(fake);

    const result = await call(
      { name: 'foo.create', method: 'POST', path: '/foos', reply: 'created' },
      undefined,
      {},
      undefined,
    );

    expect(result).toEqual({ location: 'https://api.test/foos/9' });
  });

  test('a reply with nothing to return is undefined', async () => {
    const fake = fakeFetch(() => new Response(null, { status: 204 }));
    use(fake);

    const result = await call(
      { name: 'foo.delete', method: 'DELETE', path: '/foos/1' },
      undefined,
      undefined,
      undefined,
    );

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

    const error = apiErrorOf(await call(post, undefined, {}, undefined).catch((caught: unknown) => caught));

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

    const error = apiErrorOf(
      await call(get('/foos'), undefined, undefined, undefined).catch((caught: unknown) => caught),
    );

    expect(error.status).toBe(502);
    expect(error.problem).toBeUndefined();
    expect(error.fieldErrors).toEqual({});
  });

  test('a network failure is an ApiError with status 0 and the cause', async () => {
    const cause = new TypeError('fetch failed');
    use(fakeFetch(() => Promise.reject(cause)));

    const error = apiErrorOf(
      await call(get('/foos'), undefined, undefined, undefined).catch((caught: unknown) => caught),
    );

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

    await expect(call(get('/foos/1'), undefined, undefined, undefined)).rejects.toBeInstanceOf(ApiError);
    await expect(call(get('/foos/1'), undefined, undefined, { onError: false })).rejects.toBeInstanceOf(ApiError);

    expect(seen).toHaveLength(1);
    expect(seen[0]?.status).toBe(404);
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

    const pending = call(get('/foos'), undefined, undefined, { signal: controller.signal });
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

    const first = call<{ items: number[] }>(get('/foos'), undefined, undefined, undefined);
    const second = call<{ items: number[] }>(get('/foos'), undefined, undefined, undefined);
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

    const first = call(post, undefined, { filter: { a: 1, b: 2 }, page: 1 }, undefined);
    const second = call(post, undefined, { filter: { a: 1, b: 2 }, page: 1 }, undefined);
    held.open(json([]));
    await Promise.all([first, second]);

    expect(fake.sent).toHaveLength(1);
  });

  test('a body changed after its call started does not change what it is compared with', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const body = { page: 1 };
    const first = call(post, undefined, body, undefined);
    body.page = 2;
    const second = call(post, undefined, { page: 2 }, undefined);
    held.open(json([]));
    await Promise.all([first, second]);

    expect(fake.sent).toHaveLength(2);
  });

  test('the same members in another order are another request, which costs a request and nothing else', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [
      call(post, undefined, { page: 1, size: 5 }, undefined),
      call(post, undefined, { size: 5, page: 1 }, undefined),
    ];
    held.open(json([]));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(2);
  });

  test('another operation with the same URL and body is another request', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [
      call({ ...get('/foos'), name: 'foo.one' }, undefined, undefined, undefined),
      call({ ...get('/foos'), name: 'foo.two' }, undefined, undefined, undefined),
    ];
    held.open(json({}));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(2);
  });

  test('calls that differ in body, URL, or a header are different requests', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [
      call(post, undefined, { page: 1 }, undefined),
      call(post, undefined, { page: 2 }, undefined),
      call(get('/foos'), { page: 1 }, undefined, undefined),
      call(get('/foos'), { page: 2 }, undefined, undefined),
      call(get('/foos'), { page: 2 }, undefined, { headers: { 'X-Foo': 'bar' } }),
    ];
    held.open(json({}));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(5);
  });

  test('dedupe: false sends its own request, and is not shared', async () => {
    const held = gate();
    const fake = fakeFetch(() => held.wait());
    use(fake);

    const calls = [
      call(get('/foos'), undefined, undefined, undefined),
      call(get('/foos'), undefined, undefined, { dedupe: false }),
      call(get('/foos'), undefined, undefined, undefined),
    ];
    held.open(json({}));
    await Promise.all(calls);

    expect(fake.sent).toHaveLength(2);
  });

  test('only while in flight: a call after the first one is done sends again', async () => {
    const fake = fakeFetch(() => json({}));
    use(fake);

    await call(get('/foos'), undefined, undefined, undefined);
    await call(get('/foos'), undefined, undefined, undefined);

    expect(fake.sent).toHaveLength(2);
  });

  test('a different fetch is a different set of requests', async () => {
    const held = gate();
    const one = fakeFetch(() => held.wait());
    const other = fakeFetch(() => held.wait());

    use(one);
    const first = call(get('/foos'), undefined, undefined, undefined);
    use(other);
    const second = call(get('/foos'), undefined, undefined, undefined);
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

    const first = call(get('/foos'), undefined, undefined, undefined).catch((caught: unknown) => caught);
    const second = call(get('/foos'), undefined, undefined, undefined).catch((caught: unknown) => caught);
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

    const aborts = call(get('/foos'), undefined, undefined, { signal: controller.signal }).catch(
      (caught: unknown) => caught,
    );
    const stays = call<{ ok: boolean }>(get('/foos'), undefined, undefined, undefined);
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

    const first = call(get('/foos'), undefined, undefined, { signal: one.signal }).catch((caught: unknown) => caught);
    const second = call(get('/foos'), undefined, undefined, { signal: two.signal }).catch((caught: unknown) => caught);
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

    const first = call(get('/foos'), undefined, undefined, undefined);
    const second = call(get('/foos'), undefined, undefined, undefined);
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

  const first = call(get('/foos'), undefined, undefined, undefined);
  current = late;
  await first;
  await call(get('/foos'), undefined, undefined, undefined);

  expect(early.sent).toHaveLength(1);
  expect(late.sent).toHaveLength(1);
});
