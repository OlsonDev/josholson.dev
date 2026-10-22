# Database

PostgreSQL 19 (release candidate). The migrations in `Database/Migrations` are the source of truth for structure. This document explains intent only. SQL conventions: `.claude/rules/sql.md`.

## Schemas

Migrations are ordered so each schema depends only on earlier ones.

| Migration | Schema | Contents |
| - | - | - |
| V1 | `temporal` | `year_optional_month` domain |
| V2 | `blob` | `blob`, `mime_type`, `file_extension`, `mime_type_file_extension` |
| V3 | `tag` | `tag`, `alias` |
| V4 | `auth` | `user`, `user_email`, `identity`, `session` |
| V5 | `form` | `contact` |
| V6 | `resume` | JSON Resume superset, skills, `work_tag`, `project_tag` |
| V7 | `blog` | `series`, `post`, `post_tag` |
| R | `blob` | reference data upserts (mime types, extensions) |

## Design notes

- **Resume model.** Each resume owns its section rows (`resume_id` on every section table). Cloning a resume copies them. One resume is flagged default and is shown at `/resume`; others are reachable by slug, e.g. `/resume/customized-for-employer-foo`. `private_notes` records what a targeted resume is for. Every section row has `is_visible` so an entry can be hidden on one resume without deleting it. See [ADR 0011](adr/0011-resumes-own-their-sections.md).
- **JSON Resume mapping.** `resume.resume` holds `basics` (location as `location_{field}`), `profile` holds `basics.profiles[]`, `reference.testimonial` maps to `references[].reference`, `award.date_awarded` to `awards[].date`, and `certificate.date_issued` to `certificates[].date`. `highlights`, `courses`, `roles`, and `keywords` are `TEXT[]`.
- **Skills.** A skill card has one `skill_group` (a JSON Resume `skills[].name`; the skills in it are its `keywords`) and many tags. A composite foreign key stops a skill pointing at another resume's group. `rating` is stored in half-star units, 0 to 10. The Skills page uses the default resume's skills.
- **Tags.** One global list in `tag.tag` with `tag.alias` for alternate names. Junction tables live with the entity they tag (`resume.skill_tag`, `resume.work_tag`, `resume.project_tag`, `blog.post_tag`). Pages list only the tags in use for that entity. See [ADR 0010](adr/0010-global-tags-with-aliases.md).
- **Blobs.** `blob.blob` holds metadata only, including an optional `thumbhash` (raw bytes; encode at the API boundary) for image placeholders. Files live on a server volume, named by SHA-256, so they are immutable and shared. Referencing foreign keys are `RESTRICT`; an unreferenced-blob sweep is still to be written. See [ADR 0008](adr/0008-content-addressed-blobs.md).
- **Partial dates.** `temporal.year_optional_month` is `YYYY` or `YYYY-MM`. Open end dates are `NULL`, meaning present. The `end_date >= start_date` checks compare years only. See [ADR 0007](adr/0007-year-optional-month-domain.md).
- **Keys.** `INT GENERATED ALWAYS AS IDENTITY` everywhere. Loaders must use `OVERRIDING SYSTEM VALUE` and then `setval`. See [ADR 0006](adr/0006-int-identity-always.md).
- **Enum-like columns** are `SMALLINT` holding a 1-based C# enum value, with a range check ([ADR 0012](adr/0012-enum-like-columns-as-smallint.md)). The C# enum is authoritative; this table mirrors the initial mapping:

  | Column | Values |
  | - | - |
  | `resume.work.employment_type` | 1 FullTime, 2 PartTime, 3 Contract, 4 Freelance, 5 Internship |
  | `resume.publication.type` | 1 Article, 2 Book, 3 Paper, 4 Talk, 5 Other |
  | `resume.language.proficiency` | 1 A1, 2 A2, 3 B1, 4 B2, 5 C1, 6 C2, 7 Native |
  | `resume.project.status` | 1 Active, 2 Paused, 3 Archived |
- **Reference data** is in a repeatable migration, not in seeds ([ADR 0009](adr/0009-reference-data-in-repeatable-migrations.md)). Upserts never delete, and they burn identity values on rerun.

## Not decided yet

- `sort_order` strategy. Currently `INT`. Options: integers with gaps, fractional-index strings (`TEXT COLLATE "C"`), or rewrite the whole ordered list in one statement.
- Skill usage periods (a `daterange` with `WITHOUT OVERLAPS`) to derive years used, instead of `first_used` and `last_used`.
- What maintains `updated_utc` columns: the app, a trigger, or history tracking.
- Passkey fields for `auth.identity` (currently a `data JSONB` column).
- Whether an alias may equal another tag's name (nothing enforces this today).