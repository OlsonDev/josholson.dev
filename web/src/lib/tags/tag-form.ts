import { isSlug } from '#lib/admin/slug.ts';
import type { TagRequest, TagResponse } from '#lib/api/schema.ts';
import { isPaletteColor } from '#lib/palette/colors.ts';

// The tag form's values and rules, in the shape every admin form module has, so a page imports it whole
// (`import * as tagForm from './tag-form.ts'`) and reads `tagForm.blank()`: `Values`, `blank()` (a new one),
// `fromResponse(x)` (an existing one), `toRequest(values, ...)` (what saves them), and `validate(values, ...)` (the rules
// the API enforces too). Used by: `TagEditor`.

/** What the tag form edits: the tag's fields as the inputs hold them (text for every one, and a list for aliases). */
export type Values = {
  name: string;
  slug: string;
  description: string;
  color: string | null;
  aliases: string[];
  previousSlugs: string[];
};

/** The values for a new tag: nothing in any field, and no color chosen. */
export function blank(): Values {
  return { name: '', slug: '', description: '', color: null, aliases: [], previousSlugs: [] };
}

/**
 * The values of an existing tag. The lists are the response's own, not copies: `FormState` clones what it is given, so
 * hand the result to one (or to its `reset`) and not to anything that edits it in place.
 */
export function fromResponse(tag: TagResponse): Values {
  return {
    name: tag.name,
    slug: tag.slug,
    description: tag.description ?? '',
    color: tag.color,
    aliases: tag.aliases,
    previousSlugs: tag.previousSlugs,
  };
}

/**
 * The request that saves the values. Text is trimmed and a blank description is none, as the API would do anyway. Blank
 * aliases are left out (a row the person added and never filled). `expectedVersion` is the version the form was loaded at,
 * or null to overwrite whatever is there.
 */
export function toRequest(values: Values, expectedVersion: number | null): TagRequest {
  const description = values.description.trim();
  return {
    name: values.name.trim(),
    slug: values.slug.trim(),
    description: description === '' ? null : description,
    color: values.color,
    aliases: values.aliases.map((alias) => alias.trim()).filter((alias) => alias !== ''),
    previousSlugs: values.previousSlugs,
    expectedVersion,
  };
}

/**
 * The same rules the API enforces, so most mistakes show before the round trip, keyed by field the way the API's errors
 * are (`aliases[1]`). `others` are the other tags the page already has: a name, slug, or alias another tag has is refused
 * here first, and the API still checks last (two people can race). `tagId` is the tag being edited, or null for a new one,
 * so a tag does not collide with itself. Tests mirror the API's, and Playwright runs both ends.
 */
export function validate(
  values: Values,
  tagId: number | null,
  others: readonly TagResponse[],
): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const add = (field: string, message: string): void => {
    (errors[field] ??= []).push(message);
  };
  const rest = others.filter((other) => other.tagId !== tagId);

  const name = values.name.trim();
  const lowerName = name.toLowerCase();
  if (name === '') {
    add('name', 'Name is required.');
  } else if (rest.some((other) => other.name.toLowerCase() === lowerName)) {
    add('name', 'Name is already in use.');
  }

  const slug = values.slug.trim();
  if (slug === '') {
    add('slug', 'Slug is required.');
  } else if (!isSlug(slug)) {
    add('slug', 'Slug must be lowercase words joined by single hyphens, such as foo-bar-42.');
  } else if (rest.some((other) => other.slug === slug || other.previousSlugs.includes(slug))) {
    add('slug', 'Slug is already in use.');
  }

  if (values.color === null) {
    add('color', 'Color is required.');
  } else if (!isPaletteColor(values.color)) {
    add('color', 'Color must be a Tailwind color.');
  }

  const takenByOthers = new Set(
    rest.flatMap((other) => [other.name.toLowerCase(), ...other.aliases.map((alias) => alias.toLowerCase())]),
  );
  const seen = new Set<string>();
  for (const [index, raw] of values.aliases.entries()) {
    const field = `aliases[${index.toString()}]`;
    const alias = raw.trim();
    const lower = alias.toLowerCase();
    if (alias === '') {
      add(field, 'Alias must not be empty.');
    } else if (seen.has(lower)) {
      add(field, 'Alias is listed twice.');
    } else if (lower === lowerName) {
      add(field, "Alias repeats the tag's name.");
    } else if (takenByOthers.has(lower)) {
      add(field, 'Alias is already the name or alias of another tag.');
    }
    seen.add(lower);
  }

  return errors;
}
