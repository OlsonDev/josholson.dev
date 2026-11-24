/** Where the tags are: the list, a tag's pane, and the sections of a tag. `search` is the list's own (`?page=2`), carried along. */
export const tagsPath = '/admin/tags';

export type TagSection = 'details' | 'used-by';

/** The list, at the page the search says. */
export function listHref(search = ''): string {
  return `${tagsPath}${search}`;
}

/** A tag's pane (`+` for a new one) and one of its sections, keeping the list's page so closing returns to it. */
export function paneHref(tagId: number | '+', search = '', section: TagSection = 'details'): string {
  return `${tagsPath}/${tagId.toString()}${section === 'used-by' ? '/used-by' : ''}${search}`;
}
