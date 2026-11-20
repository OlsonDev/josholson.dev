import { enrollment, nullOnApiError, signIn } from '#lib/api/index.ts';
import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// What this page offers: setting up (the first passkey) while the API says no passkey exists, and otherwise a sign-in with
// a passkey. Someone who is already signed in has no reason to be here.
export const load: PageLoad = async () => {
  // null: nobody is signed in (a 401). undefined: the API could not say, so it is not that nobody is signed in.
  const user = await signIn.me({ onError: (error) => (error.is(401) ? null : undefined) });
  if (user === undefined) return { apiAvailable: false, setup: null };
  if (user !== null) redirect(307, '/admin');

  // If it cannot be asked, the page offers the sign-in.
  return { apiAvailable: true, setup: await enrollment.status(nullOnApiError) };
};
