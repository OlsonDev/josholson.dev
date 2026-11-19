import { enrollment, nullOnApiError, signIn } from '#lib/api/index.ts';
import type { PageLoad } from './$types';

// What the page can offer: the admin area to someone signed in, setting up (the first passkey) while the API says it is
// open, and otherwise a sign-in with a passkey.
export const load: PageLoad = async () => {
  // null: nobody is signed in (a 401). undefined: the API could not say, so it is not that nobody is signed in.
  const user = await signIn.me({ onError: (error) => (error.is(401) ? null : undefined) });
  if (user === undefined) return { user: null, apiAvailable: false, setup: null };
  if (user !== null) return { user, apiAvailable: true, setup: null };

  // Asked only of someone who is not signed in. If it cannot be asked, the page offers the sign-in.
  return { user: null, apiAvailable: true, setup: await enrollment.status(nullOnApiError) };
};
