import { signIn } from '#lib/api/index.ts';
import type { PageLoad } from './$types';

// Whether anyone is signed in. The page decides between registering a passkey (a link with a token) and signing in itself.
export const load: PageLoad = async () => {
  // null: nobody is signed in (a 401). undefined: the API could not say, so it is not that nobody is signed in.
  const user = await signIn.me({ onError: (error) => (error.is(401) ? null : undefined) });
  return { user: user ?? null, apiAvailable: user !== undefined };
};
