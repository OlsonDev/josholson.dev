# ADR 0010: Global tags with aliases

- Status: Accepted
- Date: 2026-10-21

## Context

Skills and blog posts both need tags, and an "everything about PostgreSQL" view across entities is desirable.

## Decision

One global `tag.tag` table plus `tag.alias` for alternate names. Each entity has its own junction table, living in that entity's schema, with a composite primary key and a reverse index. No `entity_type` or `kind` column: the Skills page lists only tags that have a `skill_tag` row. A skill's single group is a separate concept (`resume.skill_group`).

## Alternatives considered

- Per-entity tag tables: tags cannot be shared or linked.
- A polymorphic `tagging(tag_id, entity_type, entity_id)` table: no foreign key integrity.
- `text[]` or `jsonb` on the entity: no integrity, renames touch every row, and tags cannot carry data.

## Consequences

- The admin editor for a resume's skills lists every tag, including blog-only ones; accepted as noise.
- Tag and alias names are unique case-insensitively. The API refuses an alias that equals another tag's name or alias, or the tag's own name ([ADR 0046, slug history, versions, and rendered Markdown](0046-tag-slug-history-versions-and-rendered-markdown.md)); the database does not.
- Deleting a tag cascades to its junction rows.
