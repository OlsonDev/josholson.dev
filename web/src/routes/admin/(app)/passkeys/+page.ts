import { redirect } from '@sveltejs/kit';

import { passkey } from '#lib/api/index.ts';

import type { PageLoad } from './$types';

export const load: PageLoad = async () => {
  // null: nobody is signed in (a 401), so this is not the page to be on. undefined: the API could not say.
  const passkeys = await passkey.getAll({ onError: (error) => (error.is(401) ? null : undefined) });
  if (passkeys === null) redirect(307, '/admin/sign-in');
  return { passkeys: passkeys ?? [], apiAvailable: passkeys !== undefined };
};
