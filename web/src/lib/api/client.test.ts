import { afterEach, expect, test } from 'bun:test';
import { ApiError, nullOnApiError, passkey, tag } from './index.ts';
import { configureApi, resetApi } from './runtime/index.ts';

// The generated client over a fake `fetch`: what the generator wrote, and the runtime it calls, together.
function reply(body: unknown, status = 200): typeof globalThis.fetch {
  const send = (): Promise<Response> =>
    Promise.resolve(
      new Response(JSON.stringify(body), {
        status,
        headers: { 'content-type': status < 400 ? 'application/json' : 'application/problem+json' },
      }),
    );
  return Object.assign(send, { preconnect: () => undefined });
}

afterEach(() => {
  resetApi();
});

test('a response with instants in it, through its schema constant, has Temporal.Instant', async () => {
  configureApi({
    baseUrl: 'https://api.test',
    fetch: () =>
      reply([
        { passkeyId: 1, nickname: 'red', createdUtc: '2026-01-02T03:04:05Z', lastUsedUtc: null, transports: [4] },
        { passkeyId: 2, nickname: 'blue', createdUtc: '2026-02-03T04:05:06Z', lastUsedUtc: '2026-03-04T05:06:07Z' },
      ]),
  });

  const passkeys = await passkey.getAll();

  expect(passkeys[0]?.createdUtc.toString()).toBe('2026-01-02T03:04:05Z');
  expect(passkeys[0]?.lastUsedUtc).toBeNull();
  expect(passkeys[1]?.lastUsedUtc?.toString()).toBe('2026-03-04T05:06:07Z');
});

test('a response with no instants is returned as it was sent', async () => {
  const foo = { tagId: 3, name: 'Foo', slug: 'foo', description: null, color: null };
  configureApi({ baseUrl: 'https://api.test', fetch: () => reply(foo) });

  expect(await tag.get({ tagId: 3 })).toEqual(foo);
});

test('nullOnApiError makes a failed call null, and a call without it throws', async () => {
  configureApi({ baseUrl: 'https://api.test', fetch: () => reply({ title: 'Boom', status: 500 }, 500) });

  expect(await tag.getAll(nullOnApiError)).toBeNull();
  await expect(tag.getAll()).rejects.toBeInstanceOf(ApiError);
});
