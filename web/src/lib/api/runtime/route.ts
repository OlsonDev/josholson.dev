/** What a path or query parameter can be: something with one obvious text. */
export type ParamValue = string | number | boolean | Temporal.Instant;

/** The query parameters of a call: an array repeats its key, and `undefined` and `null` are left out. */
export type Query = Readonly<Record<string, unknown>>;

/**
 * A tag for a route with its path parameters in it: `` route`/tags/${tagId}` `` is `/tags/7`. Each value is encoded
 * as one path segment (`a/b` is `a%2Fb`). A value that is missing is a bug in the caller, so it throws.
 */
export function route(parts: TemplateStringsArray, ...values: ParamValue[]): string {
  let path = parts[0] ?? '';
  for (const [index, value] of values.entries()) {
    // The types say it cannot happen, and data from a form or a URL is not always typed.
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- a caller in JavaScript, or a cast
    if (value === undefined || value === null) throw new Error(`A path parameter has no value: ${parts.join('{}')}`);
    path += encodeURIComponent(queryText(value)) + (parts[index + 1] ?? '');
  }
  return path;
}

/** The route followed by its query string, when there is anything to put in one. */
export function withQuery(uri: string, query: Query | undefined): string {
  if (query === undefined) return uri;
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value === undefined || value === null) continue;
    for (const item of Array.isArray(value) ? value : [value]) search.append(name, queryText(item));
  }
  const text = search.toString();
  return text === '' ? uri : `${uri}?${text}`;
}

function queryText(value: unknown): string {
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  // The type tag, not `instanceof Temporal.Instant`: that needs the global, which is not there until `ensureTemporal`
  // has run, and a polyfill's class is not a native one. Both set the tag, as the specification says.
  if (Object.prototype.toString.call(value) === '[object Temporal.Instant]') return String(value);
  throw new Error('A path or query parameter is a string, a number, a boolean, or an instant.');
}
