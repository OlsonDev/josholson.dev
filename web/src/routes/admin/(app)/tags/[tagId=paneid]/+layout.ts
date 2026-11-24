import { error } from '@sveltejs/kit';

import { tag } from '#lib/api/index.ts';

import type { LayoutLoad } from './$types';

// The same for both sections of the tag. `+` is a new tag. Otherwise the tag, and what uses it, or a 404.
export const load: LayoutLoad = async ({ params, depends }) => {
  depends('app:tag');
  if (params.tagId === '+') return { tag: null, usage: null };

  const tagId = Number(params.tagId);
  const notFound = { onError: (failure: { is: (status: number) => boolean }) => (failure.is(404) ? null : undefined) };
  const [found, usage] = await Promise.all([tag.get({ tagId }, notFound), tag.getDeletionImpact({ tagId }, notFound)]);
  if (found === null || usage === null) error(404, 'There is no such tag.');
  if (found === undefined || usage === undefined) error(503, 'The API did not answer.');
  return { tag: found, usage };
};
