import { tag } from '#lib/api/index.ts';

import type { LayoutLoad } from './$types';

// The list stays on screen while a tag is edited beside it, so the tags are loaded here and not by each page. Saving or
// deleting a tag reloads them with `invalidate('app:tags')`.
export const load: LayoutLoad = async ({ depends }) => {
  depends('app:tags');
  return { tags: await tag.getAll() };
};
