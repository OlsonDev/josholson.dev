import { error, redirect } from '@sveltejs/kit';

import { signIn } from '#lib/api/index.ts';

import type { LayoutLoad } from './$types';

// Every page in the admin area needs a session. Without one the visitor is sent to the sign-in, which is also where setup
// is offered while no passkey exists. A session that the API cannot confirm is an error page and not a sign-in: the API
// not answering is not the same as nobody being signed in.
export const load: LayoutLoad = async () => {
  // null: nobody is signed in (a 401). undefined: the API could not say.
  const user = await signIn.me({ onError: (failure) => (failure.is(401) ? null : undefined) });
  if (user === null) redirect(307, '/admin/sign-in');
  if (user === undefined) error(503, 'The API is not answering, so nothing here can work right now.');
  return { user };
};
