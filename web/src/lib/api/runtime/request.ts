import { ApiError, type Problem } from './api-error.ts';
import { revive, type Shape } from './revive.ts';

/** Options every generated function takes last. */
export type CallOptions = {
  /** Cancels this call. Calls sharing the same request carry on, and the request stops when none is left. */
  signal?: AbortSignal;
  /** Headers for this call, over the defaults. */
  headers?: Readonly<Record<string, string>>;
  /** Shares a request identical to one already in flight, and lets later ones share this one. Default `true`. */
  dedupe?: boolean;
  /** `false` keeps the global `onError` hook from seeing this call's error, for a caller that handles it itself. */
  onError?: false;
};

/** What a create (`201`) returns: where the new thing is. */
export type Created = { location: string };

/** A request the runtime refused to send twice at once, found while it was in flight. */
export type DuplicateRequest = {
  operation: string;
  method: string;
  url: string;
  /** The stack of the call that started the request. */
  first: string;
  /** The stack of the call that joined it. */
  second: string;
};

/** What differs between one running of the runtime and another. Set once per context (SSR, browser, a test). */
export type ApiConfig = {
  /**
   * Where the API is, with no trailing slash (`''` is the page's own origin). There is no default: a call made before it
   * is set, such as one that runs before SvelteKit's `init` hook has finished, would go to the wrong place, so it fails.
   */
  baseUrl: string | undefined;
  /**
   * Gives the `fetch` to send with. A function, because on the server it is the request's own (`event.fetch`, which
   * SvelteKit only hands out during that request), and it is asked for at the moment a call starts.
   */
  fetch: () => typeof globalThis.fetch;
  /** Sees every `ApiError` before it is thrown, for what the whole app does about one (a sign-in page, a toast). */
  onError: ((error: ApiError) => void) | undefined;
  /** Sees a call that joined a request already in flight, which is usually a component asking twice. */
  onDuplicate: ((duplicate: DuplicateRequest) => void) | undefined;
};

export type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

/** What a generated function says about its operation. */
export type OperationSpec = {
  /** `controller.action`, as in the OpenAPI document. */
  name: string;
  method: Method;
  /** The route, with `{name}` where a path parameter goes. */
  path: string;
  /** What the response holds: JSON, or (for a `201`) only the `Location`. Left out when there is nothing to return. */
  reply?: 'json' | 'created';
  /** Where the JSON has instants. */
  revive?: Shape;
};

type Params = Readonly<Record<string, unknown>>;

type RawResponse = {
  ok: boolean;
  status: number;
  contentType: string | null;
  location: string | null;
  text: string;
};

type InFlight = {
  // What the request is, kept as the text that was sent, so a later call is compared with it and nothing is built to
  // compare. A caller changing its body object afterward cannot change it.
  url: string;
  bodyText: string | undefined;
  headers: Record<string, string>;
  promise: Promise<RawResponse>;
  controller: AbortController;
  callers: number;
  settled: boolean;
  firstCaller: string | undefined;
};

const defaults = (): ApiConfig => ({
  baseUrl: undefined,
  fetch: () => globalThis.fetch,
  onError: undefined,
  onDuplicate: undefined,
});

let config = defaults();

// In-flight requests, per `fetch` and then per operation (`controller.action`). On the server each request has its own
// `fetch`, so one user's call is never another's. An operation with nothing in flight has no entry.
const inFlightByFetch = new WeakMap<object, Map<string, InFlight[]>>();

/** Sets what differs in this context. Call it from SvelteKit's `init` hook, or from a test's setup. */
export function configureApi(changes: Partial<ApiConfig>): void {
  config = { ...config, ...changes };
}

/** Puts every setting back to its default. For tests. */
export function resetApi(): void {
  config = defaults();
}

/** What every generated function calls. Not for app code. */
export function call<TResult>(
  spec: OperationSpec,
  params: Params | undefined,
  body: unknown,
  options: CallOptions | undefined,
): Promise<TResult> {
  const settings = config;
  // Now, before anything is awaited: SvelteKit's `getRequestEvent()` is only valid synchronously on some runtimes.
  const send = settings.fetch();
  // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- the generated function declares the type
  return perform(spec, settings, send, params, body, options ?? {}) as Promise<TResult>;
}

async function perform(
  spec: OperationSpec,
  settings: ApiConfig,
  send: typeof globalThis.fetch,
  params: Params | undefined,
  body: unknown,
  options: CallOptions,
): Promise<unknown> {
  if (settings.baseUrl === undefined) {
    throw new Error(
      `${spec.name} was called before the API client was configured: call configureApi first (the init hook).`,
    );
  }
  const url = settings.baseUrl + buildPath(spec, params);
  const bodyText = body === undefined ? undefined : JSON.stringify(body);
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...(bodyText === undefined ? {} : { 'Content-Type': 'application/json' }),
    ...options.headers,
  };
  const exchange = (signal: AbortSignal | undefined): Promise<RawResponse> =>
    exchangeWith(spec, send, url, bodyText, headers, signal);

  let raw: RawResponse;
  try {
    raw =
      options.dedupe === false
        ? await exchange(options.signal)
        : await shared(spec, settings, send, url, bodyText, headers, exchange, options.signal);
    if (!raw.ok) throw failure(spec, raw);
  } catch (error) {
    if (error instanceof ApiError && options.onError !== false) settings.onError?.(error);
    throw error;
  }
  return result(spec, raw);
}

// Sends one request and reads its body. The network failing is an `ApiError` with no status; an abort is left as it is.
async function exchangeWith(
  spec: OperationSpec,
  send: typeof globalThis.fetch,
  url: string,
  bodyText: string | undefined,
  headers: Record<string, string>,
  signal: AbortSignal | undefined,
): Promise<RawResponse> {
  try {
    const response = await send(url, { method: spec.method, headers, body: bodyText, credentials: 'include', signal });
    return {
      ok: response.ok,
      status: response.status,
      contentType: response.headers.get('content-type'),
      location: response.headers.get('location'),
      text: await response.text(),
    };
  } catch (error) {
    if (signal?.aborted === true || (error instanceof DOMException && error.name === 'AbortError')) throw error;
    throw new ApiError(spec.name, 0, undefined, { cause: error });
  }
}

function failure(spec: OperationSpec, raw: RawResponse): ApiError {
  let problem: Problem | undefined;
  if (raw.contentType?.includes('json') === true) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- the API sends problem details
      problem = JSON.parse(raw.text) as Problem;
    } catch {
      // The body said it was JSON and was not, so there are no problem details.
    }
  }
  return new ApiError(spec.name, raw.status, problem);
}

// Each caller parses the shared text itself, so each owns its result and may change it.
function result(spec: OperationSpec, raw: RawResponse): unknown {
  if (spec.reply === 'created') return { location: raw.location ?? '' } satisfies Created;
  if (spec.reply !== 'json') return undefined;
  const value: unknown = JSON.parse(raw.text);
  return spec.revive === undefined ? value : revive(value, spec.revive);
}

// The same request, in flight at once, is sent once. Only while it is in flight: a later call asks again. Most calls
// have nothing in flight for their operation, so most cost one map lookup.
function shared(
  spec: OperationSpec,
  settings: ApiConfig,
  send: typeof globalThis.fetch,
  url: string,
  bodyText: string | undefined,
  headers: Record<string, string>,
  exchange: (signal: AbortSignal | undefined) => Promise<RawResponse>,
  signal: AbortSignal | undefined,
): Promise<RawResponse> {
  let byOperation = inFlightByFetch.get(send);
  if (byOperation === undefined) {
    byOperation = new Map();
    inFlightByFetch.set(send, byOperation);
  }
  const requests = byOperation;
  const caller = settings.onDuplicate === undefined ? undefined : (new Error('call').stack ?? '');

  const existing = requests
    .get(spec.name)
    ?.find((entry) => entry.url === url && entry.bodyText === bodyText && sameHeaders(entry.headers, headers));
  if (existing !== undefined) {
    existing.callers++;
    settings.onDuplicate?.({
      operation: spec.name,
      method: spec.method,
      url,
      first: existing.firstCaller ?? '',
      second: caller ?? '',
    });
    return waitFor(existing, signal, () => {
      release(requests, spec.name, existing);
    });
  }

  const controller = new AbortController();
  const promise = exchange(controller.signal).finally(() => {
    entry.settled = true;
    remove(requests, spec.name, entry);
  });
  const entry: InFlight = {
    url,
    bodyText,
    headers,
    promise,
    controller,
    callers: 1,
    settled: false,
    firstCaller: caller,
  };
  const inOperation = requests.get(spec.name);
  if (inOperation === undefined) requests.set(spec.name, [entry]);
  else inOperation.push(entry);
  return waitFor(entry, signal, () => {
    release(requests, spec.name, entry);
  });
}

// A caller that aborts stops waiting. The request itself stops only when no caller is left.
function release(requests: Map<string, InFlight[]>, operation: string, entry: InFlight): void {
  if (entry.settled) return;
  entry.callers--;
  if (entry.callers > 0) return;
  entry.controller.abort();
  remove(requests, operation, entry);
}

function remove(requests: Map<string, InFlight[]>, operation: string, entry: InFlight): void {
  const inOperation = requests.get(operation);
  const index = inOperation?.indexOf(entry) ?? -1;
  if (inOperation === undefined || index < 0) return;
  inOperation.splice(index, 1);
  if (inOperation.length === 0) requests.delete(operation);
}

function sameHeaders(a: Record<string, string>, b: Record<string, string>): boolean {
  const names = Object.keys(a);
  return names.length === Object.keys(b).length && names.every((name) => a[name] === b[name]);
}

async function waitFor(entry: InFlight, signal: AbortSignal | undefined, leave: () => void): Promise<RawResponse> {
  if (signal === undefined) return await entry.promise;
  if (signal.aborted) {
    leave();
    throw signal.reason;
  }
  return await new Promise<RawResponse>((resolve, reject) => {
    const onAbort = (): void => {
      leave();
      reject(signal.reason);
    };
    signal.addEventListener('abort', onAbort, { once: true });
    void entry.promise.then(resolve, reject).finally(() => {
      signal.removeEventListener('abort', onAbort);
    });
  });
}

// The route with its path parameters filled in, and every other parameter as the query string.
function buildPath(spec: OperationSpec, params: Params | undefined): string {
  const used = new Set<string>();
  const path = spec.path.replace(/\{([^}]+)\}/g, (_match, name: string) => {
    const value = params?.[name];
    if (value === undefined || value === null) throw new Error(`${spec.name} needs a value for ${name}`);
    used.add(name);
    return encodeURIComponent(queryText(value));
  });
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries(params ?? {})) {
    if (used.has(name) || value === undefined || value === null) continue;
    for (const item of Array.isArray(value) ? value : [value]) query.append(name, queryText(item));
  }
  const text = query.toString();
  return text === '' ? path : `${path}?${text}`;
}

function queryText(value: unknown): string {
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  // The type tag, not `instanceof Temporal.Instant`: that needs the global, which is not there until `ensureTemporal`
  // has run, and a polyfill's class is not a native one. Both set the tag, as the specification says.
  if (Object.prototype.toString.call(value) === '[object Temporal.Instant]') return String(value);
  throw new Error('A path or query parameter is a string, a number, a boolean, or an instant.');
}
