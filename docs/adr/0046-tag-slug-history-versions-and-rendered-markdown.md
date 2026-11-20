# ADR 0046: Tag slug history, saves checked by version, and Markdown rendered when it is written

- Status: Accepted
- Date: 2026-11-19

## Context

The tag editor in the admin ([ADR 0010, global tags with aliases](0010-global-tags-with-aliases.md) is the model) raised four questions the first `tag.tag` could not answer:

- A slug is a URL. Changing one must not break a link someone already has, and a slug a tag gave up must not be taken by another tag, or the old link would land on the wrong page.
- Two browser tabs (or a phone and a desktop) can edit the same tag, and the later save would silently undo the earlier one.
- A description is Markdown that the public site and the admin show, rich on hover, and a browser should not need a Markdown renderer or a sanitizer to show it.
- A tag's color is the light-mode background as a Tailwind name, but the API still validated a hex code.

## Decision

- **Slugs have a history.** `tag.slug` holds every slug a tag has had, `slug` unique across all of them, with `is_canonical` marking the current one (a partial unique index allows one per tag). The slug is no longer a column of `tag.tag`. Saving a tag with a new slug adds a row, makes it canonical, and keeps the old one as a previous slug; `GET /tags/by-slug/{slug}` answers `308` with the current URL for a previous slug. The editor lists the previous slugs, and removing one frees it for any tag. Saving a previous slug as the slug brings it back without adding a row.
  - A flag on the slug, not a `canonical_slug_id` on the tag: the tag and its first slug would need each other's key (a circular foreign key), and the seed tool, which inserts layers in foreign-key order, has no order for that. `ux_slug_one_canonical_per_tag` is the same pattern as `ux_resume_one_default`. The service keeps every tag at exactly one canonical slug, in the transaction that changes it.
  - Aliases stay a separate table: they are free-text synonyms for matching (JS for JavaScript), while a slug is a URL identity with a lower-kebab-case constraint and redirect behavior. An alias may not be another tag's name or alias, nor the tag's own name, ignoring case (this answers the open question in ADR 0010).
- **A save is checked against a version.** `tag.tag.version` starts at 1 and each update adds 1. The request carries `ExpectedVersion`; when the row's version differs, the replace is refused with `409` and the person chooses to reload or to overwrite (send `null`). A counter, not a timestamp: it compares exactly (no rounding to a microsecond across JSON), and a seed layer has nothing time-dependent to export. The same field is the pattern for the long résumé forms.
- **Markdown is rendered when it is written.** `MarkdownService` (Markdig, CommonMark with the GitHub-style extras, raw HTML escaped, then HtmlSanitizer on the output) renders on create and update, and `tag.tag.description_html` stores the result beside the Markdown (a check keeps the two null together). The browser shows it with `{@html}`, with no renderer of its own. The Markdown stays the source of truth, so a change to the renderer or sanitizer is a re-render of the stored rows. Blog posts will use the same service.
- **Color is a Tailwind name.** The payload validates the `color.tailwind` domain (`[TailwindColor]`) instead of a hex code, and a test compares its families with the domain.
- **Deleting says what it affects.** `GET /tags/{id}/deletion-impact` returns `blockers` and `warnings`, each a list of `{ kind, count }`, where the API decides which is which and the web only presents them. A delete re-checks in its own transaction and answers `409` with the same body when something blocks it. Nothing blocks a tag today (a tag on a skill, project, job, or post goes off them, which is a warning), so the blocked path is exercised by tests until something does.

## Alternatives considered

- `canonical_slug_id` on `tag.tag`: see above; it also needs a deferrable foreign key and an insert order the seed tool lacks.
- Merging slugs into aliases: one table, but one set of rules for two different jobs.
- `updated_utc` as the version: it also records when, which nobody asked for yet, and it made fixtures and exports depend on the clock.
- Rendering Markdown in the browser: a renderer and a sanitizer in the bundle, run on every page view, and the public pages are rendered on the server anyway.
- Rendering on read with a cache: no stored HTML to keep current, at a cost for each read; the stored form wins because blog posts will want the same.

## Consequences

- `tag.tag` has no slug, and every query that needs one joins `tag.slug` on `is_canonical`. A tag with no canonical slug cannot be made through the API, but the database does not forbid it.
- A previous slug blocks reuse until it is removed from the tag, on purpose.
- The Markdown column is derived data: a migration that changes the renderer's output needs a one-off re-render.
- Replacing a tag always sends the whole tag, previous slugs and aliases included, and the API replaces them.
