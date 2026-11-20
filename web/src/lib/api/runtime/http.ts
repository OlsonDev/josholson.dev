import { type CallOptions, dispatch } from './request.ts';
import type { Shape } from './revive.ts';
import type { Query } from './route.ts';

// What every generated function calls, one for each method, as `http.get(...)`. Not for app code.
//
// The arguments run from what every call has to what only some do, so a call never passes `undefined` for one it does
// not have. `options` is first because every generated function takes them. Then the operation's name
// (`controller.action`), its route (`route` over a template, with the path parameters in it), and, for a method with a
// body, the body. Then, only when the response has instants in it, the `Shape` that says where, and last a query.
//
// There is no argument for what comes back. A `201` is `{ location }`, a response with nothing in it is `undefined`, and
// anything else is JSON. A call that needs to deviate gets an option in `CallOptions`, not a parameter.

export function get<TResult>(
  options: CallOptions | undefined,
  name: string,
  uri: string,
  shape?: Shape<TResult>,
  query?: Query,
): Promise<TResult> {
  return dispatch<TResult>(options, { name, method: 'GET', shape }, uri, query, undefined);
}

// `delete` is a reserved word, so it is declared with a `$` and exported as itself.
function delete$<TResult>(
  options: CallOptions | undefined,
  name: string,
  uri: string,
  shape?: Shape<TResult>,
  query?: Query,
): Promise<TResult> {
  return dispatch<TResult>(options, { name, method: 'DELETE', shape }, uri, query, undefined);
}
export { delete$ as delete };

export function post<TResult>(
  options: CallOptions | undefined,
  name: string,
  uri: string,
  body?: unknown,
  shape?: Shape<TResult>,
  query?: Query,
): Promise<TResult> {
  return dispatch<TResult>(options, { name, method: 'POST', shape }, uri, query, body);
}

export function put<TResult>(
  options: CallOptions | undefined,
  name: string,
  uri: string,
  body?: unknown,
  shape?: Shape<TResult>,
  query?: Query,
): Promise<TResult> {
  return dispatch<TResult>(options, { name, method: 'PUT', shape }, uri, query, body);
}

export function patch<TResult>(
  options: CallOptions | undefined,
  name: string,
  uri: string,
  body?: unknown,
  shape?: Shape<TResult>,
  query?: Query,
): Promise<TResult> {
  return dispatch<TResult>(options, { name, method: 'PATCH', shape }, uri, query, body);
}
