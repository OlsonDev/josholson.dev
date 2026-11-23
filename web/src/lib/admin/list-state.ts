/** The page sizes a list offers, and the one it starts with. */
export const pageSizes = [10, 25, 50, 100] as const;
export const defaultPageSize = 10;

/** Where a list is: which page of how many, and the rows it shows (`start` up to, not including, `end`, counting from 0). */
export type Paging = {
  page: number;
  size: number;
  total: number;
  pageCount: number;
  start: number;
  end: number;
};

type Options = { sizes?: readonly number[]; defaultSize?: number };

/**
 * The paging a URL asks for, over `total` rows: `?page=2&size=25`. Anything it cannot use (no number, a size the list does
 * not offer) is the default, and a page past the end is the last one, so a stale link (the last row of the last page was
 * deleted) shows the last page and not an empty one.
 */
export function parsePaging(
  params: { get(name: string): string | null },
  total: number,
  options: Options = {},
): Paging {
  const sizes = options.sizes ?? pageSizes;
  const defaultSize = options.defaultSize ?? defaultPageSize;
  const asked = Number(params.get('size'));
  const size = sizes.includes(asked) ? asked : defaultSize;
  const pageCount = Math.max(1, Math.ceil(total / size));
  const requested = Number(params.get('page'));
  const page = Number.isInteger(requested) && requested >= 1 ? Math.min(requested, pageCount) : 1;
  const start = (page - 1) * size;
  return { page, size, total, pageCount, start, end: Math.min(start + size, total) };
}

/**
 * The query string for a page and size, with what is the default left out (`''` for the first page at the usual size), so
 * the first page of a list is its plain address.
 */
export function pagingSearch(paging: { page: number; size: number }, options: Options = {}): string {
  const defaultSize = options.defaultSize ?? defaultPageSize;
  const params = new URLSearchParams();
  if (paging.page > 1) params.set('page', paging.page.toString());
  if (paging.size !== defaultSize) params.set('size', paging.size.toString());
  const search = params.toString();
  return search === '' ? '' : `?${search}`;
}

/** How many places a pager has for page numbers and gaps, once there are more pages than that. */
export const pagerSlots = 7;

/**
 * What a pager shows: every page when they fit, and otherwise always exactly `pagerSlots` places (the first page, the last,
 * the current one with a neighbor on each side, and "gap" where numbers are left out), so a button is in the same place
 * whichever page is showing and nothing moves when you click. Near an end it shows a run of pages instead of a gap.
 */
export function visiblePages(page: number, pageCount: number): (number | 'gap')[] {
  if (pageCount <= pagerSlots) return Array.from({ length: pageCount }, (_, index) => index + 1);
  const run = pagerSlots - 2; // the places between the first page and the last
  if (page <= run - 1) return [...Array.from({ length: run }, (_, index) => index + 1), 'gap', pageCount];
  if (page >= pageCount - run + 2) {
    return [1, 'gap', ...Array.from({ length: run }, (_, index) => pageCount - run + 1 + index)];
  }
  return [1, 'gap', page - 1, page, page + 1, 'gap', pageCount];
}

/**
 * The paging in a URL, tidied: the plain form of what it says (`?page=1&size=25` is `?size=25`), and nothing else it may
 * carry. For an address that must return to the list from another page, which does not know how many rows there are, so a
 * page is kept as it is unless it is not a page at all.
 */
export function normalizeSearch(params: { get(name: string): string | null }, options: Options = {}): string {
  const sizes = options.sizes ?? pageSizes;
  const defaultSize = options.defaultSize ?? defaultPageSize;
  const asked = Number(params.get('size'));
  const requested = Number(params.get('page'));
  return pagingSearch(
    {
      page: Number.isInteger(requested) && requested >= 1 ? requested : 1,
      size: sizes.includes(asked) ? asked : defaultSize,
    },
    options,
  );
}
