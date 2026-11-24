import { dev } from '$app/env';
import { API_URL, TEST_SCOPES } from '$app/env/private';
import { getRequestEvent } from '$app/server';
import type { HandleFetch, ServerInit } from '@sveltejs/kit/hooks';

import { configureApi, ensureTemporal, warnAboutDuplicate } from '#lib/api/runtime/index.ts';
import { forwardScope } from '#lib/api/runtime/scope.ts';

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

// The calls the server makes to the API for a page (a `load`'s) carry the test scope of the page request when the server
// was started for tests (`TEST_SCOPES`), so what is drawn on the server comes from the same database as what the
// browser's own calls read (ADR 0033, a scoped database per test). Without it, this passes every call through.
export const handleFetch: HandleFetch = ({ event, request, fetch }) => {
  forwardScope(request, event.request.headers, API_URL, TEST_SCOPES);
  return fetch(request);
};
