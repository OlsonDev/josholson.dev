import { PUBLIC_API_URL } from '$app/env/public';
import { dev } from '$app/env';
import { configureApi, ensureTemporal, warnAboutDuplicate } from '#lib/api/runtime/index.ts';
import type { ClientInit } from '@sveltejs/kit/hooks';

// Sets up the API client for the browser: calls go straight to the API (it takes the session cookie, which is set for
// the whole site), with the browser's own `fetch`. Safari has no `Temporal` yet, so it fetches the polyfill here,
// once, before anything runs; every other browser skips it.
export const init: ClientInit = async () => {
  await ensureTemporal();
  configureApi({
    baseUrl: PUBLIC_API_URL,
    onDuplicate: dev ? warnAboutDuplicate : undefined,
  });
};
