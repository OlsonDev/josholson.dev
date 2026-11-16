import { getTags } from '#lib/server/api.ts';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ fetch }) => ({
  // null when the API cannot be reached, so the page renders without it
  tags: await getTags(fetch),
});
