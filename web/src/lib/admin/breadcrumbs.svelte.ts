import { getContext, setContext } from 'svelte';

/** One step of the breadcrumb trail after "Admin": a link back, or (the last one) where you are. */
export type Crumb = { label: string; href?: string };

const key = Symbol('breadcrumbs');

/**
 * The breadcrumb trail a page asks for. A page with nothing to say leaves it empty and the header shows the page's own
 * title; a page inside a page (a tag being edited, in Tags) gives the whole trail after "Admin": `Tags` as a link back (to
 * the list at the page it was on) and then the thing itself. The header, not the page, draws it.
 */
export class BreadcrumbTrail {
  items = $state.raw<readonly Crumb[]>([]);
}

/** Called once, by the admin's layout. */
export function provideBreadcrumbs(): BreadcrumbTrail {
  const trail = new BreadcrumbTrail();
  setContext(key, trail);
  return trail;
}

/** Called by `Crumbs`. */
export function useBreadcrumbs(): BreadcrumbTrail {
  const trail = getContext<BreadcrumbTrail | undefined>(key);
  if (trail === undefined) throw new Error('Breadcrumbs belong inside the admin layout.');
  return trail;
}
