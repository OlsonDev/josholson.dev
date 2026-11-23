import BriefcaseIcon from '@lucide/svelte/icons/briefcase';
import FolderKanbanIcon from '@lucide/svelte/icons/folder-kanban';
import NewspaperIcon from '@lucide/svelte/icons/newspaper';
import SparklesIcon from '@lucide/svelte/icons/sparkles';
import type { Component } from 'svelte';

import { type DeletionImpactItem, DeletionImpactKind } from '#lib/api/schema.ts';

/** How the delete confirmation names one kind of thing that refers to what is being deleted, and where to look at them. */
export type ImpactKindWords = { singular: string; plural: string; icon: Component; href: string };

/**
 * One entry for each `DeletionImpactKind` the API can send. The API decides what blocks a delete and what only warns, and
 * this is the web's half: words, an icon, and a page. A kind the API gains does not compile here until it has an entry.
 */
export const impactKinds: Record<DeletionImpactKind, ImpactKindWords> = {
  [DeletionImpactKind.Work]: { singular: 'job', plural: 'jobs', icon: BriefcaseIcon, href: '/admin/resumes' },
  [DeletionImpactKind.Project]: {
    singular: 'project',
    plural: 'projects',
    icon: FolderKanbanIcon,
    href: '/admin/projects',
  },
  [DeletionImpactKind.Skill]: { singular: 'skill', plural: 'skills', icon: SparklesIcon, href: '/admin/skills' },
  [DeletionImpactKind.Post]: { singular: 'blog post', plural: 'blog posts', icon: NewspaperIcon, href: '/admin/blog' },
};

/** "1 skill", "3 projects". */
export function describeImpactItem(item: DeletionImpactItem): string {
  const words = impactKinds[item.kind];
  return `${item.count.toString()} ${item.count === 1 ? words.singular : words.plural}`;
}
