import { dev } from '$app/env';
import { API_URL } from '$app/env/private';
import { getRequestEvent } from '$app/server';
import type { ServerInit } from '@sveltejs/kit/hooks';

import { configureApi, ensureTemporal, warnAboutDuplicate } from '#lib/api/runtime/index.ts';

// Sets up the API client for the server: calls go to the API over the compose network, with the `fetch` of the request
// that is being served (SvelteKit's `event.fetch`, which it hands out per request), so what a `load` fetches is sent
// to the browser with the page and not fetched again there.
export const init: ServerInit = async () => {
  await ensureTemporal();
  configureApi({
    baseUrl: API_URL,
    fetch: () => getRequestEvent().fetch,
    onDuplicate: dev ? warnAboutDuplicate : undefined,
  });
};
