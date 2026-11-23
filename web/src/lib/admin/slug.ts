/** A slug as the API and the database accept it: lowercase letters and digits in words joined by single hyphens. */
export const slugPattern = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** Whether the text is a slug the API accepts. */
export function isSlug(text: string): boolean {
  return slugPattern.test(text);
}

/**
 * The slug a name suggests: lowercase, anything that is not a letter or a digit becomes a hyphen, and hyphens are
 * squeezed and trimmed. Names are plain Latin text, so accents are not folded: they drop out like any other symbol.
 * An empty result (a name with no letter or digit) is not a slug, so the caller treats it as blank.
 */
export function slugify(name: string): string {
  return name
    .toLowerCase()
    .replaceAll(/[^a-z0-9]+/g, '-')
    .replaceAll(/^-+|-+$/g, '');
}

/**
 * What the slug field holds after the name changed. While the slug is still the one the old name suggested (or empty
 * along with an empty name), it follows the name. Once the person has edited it to anything else, it is theirs and stays.
 */
export function nextSlug(previousName: string, name: string, slug: string): string {
  return slug === slugify(previousName) ? slugify(name) : slug;
}
