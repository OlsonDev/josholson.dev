import { nullOnApiError, tag } from '#lib/api/index.ts';

import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async () => ({
  // null when the API cannot be reached, so the page renders without it
  tags: await tag.getAll(nullOnApiError),
});
