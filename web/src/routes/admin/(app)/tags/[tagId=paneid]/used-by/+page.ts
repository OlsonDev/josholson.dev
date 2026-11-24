import { redirect } from '@sveltejs/kit';

import type { PageLoad } from './$types';

// A new tag is used by nothing yet, so it has no section for that: back to its form, on the same page of the list.
export const load: PageLoad = ({ params, url }) => {
  if (params.tagId === '+') redirect(307, `/admin/tags/+${url.search}`);
};
