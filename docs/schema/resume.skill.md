# `resume.skill`

## Columns

| Name             | Type                                                                  |   Nullable | Default                        |
| ---------------- | --------------------------------------------------------------------- | ---------: | ------------------------------ |
| `skill_id`       | [`INTEGER`][pg:integer]                                               | `NOT NULL` | `GENERATED ALWAYS AS IDENTITY` |
| `resume_id`      | [`INTEGER`][pg:integer]                                               | `NOT NULL` |                                |
| `skill_group_id` | [`INTEGER`][pg:integer]                                               |     `NULL` |                                |
| `sort_order`     | [`INTEGER`][pg:integer]                                               | `NOT NULL` | `0`                            |
| `is_visible`     | [`BOOLEAN`][pg:boolean]                                               | `NOT NULL` | `true`                         |
| `name`           | [`text.trimmed`][domain:text.trimmed]                                 | `NOT NULL` |                                |
| `icon_blob_id`   | [`INTEGER`][pg:integer]                                               |     `NULL` |                                |
| `url`            | [`text.url`][domain:text.url]                                         |     `NULL` |                                |
| `first_used`     | [`temporal.year_optional_month`][domain:temporal.year_optional_month] |     `NULL` |                                |
| `last_used`      | [`temporal.year_optional_month`][domain:temporal.year_optional_month] |     `NULL` |                                |
| `versions`       | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `rating`         | [`SMALLINT`][pg:smallint]                                             |     `NULL` |                                |
| `is_highlight`   | [`BOOLEAN`][pg:boolean]                                               | `NOT NULL` | `false`                        |
| `notes`          | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |

## Constraints

### Primary key

| Name       | Definition |
| ---------- | ---------- |
| `pk_skill` | `skill_id` |

### Unique constraints

| Name                   | Definition        |
| ---------------------- | ----------------- |
| `uq_skill_resume_name` | `resume_id, name` |

### Check constraints

| Name                                       | Definition                                                                                                       |
| ------------------------------------------ | ---------------------------------------------------------------------------------------------------------------- |
| `ck_skill_last_used_not_before_first_used` | `(first_used IS NULL) OR (last_used IS NULL) OR ("left"((last_used)::text, 4) >= "left"((first_used)::text, 4))` |
| `ck_skill_rating_range`                    | `(rating >= 0) AND (rating <= 10)`                                                                               |

## Indexes

```sql
CREATE INDEX ix_skill_icon_blob_id
  ON resume.skill
  USING btree (icon_blob_id)
  WHERE (icon_blob_id IS NOT NULL);

CREATE INDEX ix_skill_resume_id_sort_order
  ON resume.skill
  USING btree (resume_id, sort_order);

CREATE INDEX ix_skill_skill_group_id
  ON resume.skill
  USING btree (skill_group_id)
  WHERE (skill_group_id IS NOT NULL);
```

## Relations

### Foreign keys

| Name                    | Columns                     | Ref Table                                        | Ref Columns                 | On Delete   | On Update   |
| ----------------------- | --------------------------- | ------------------------------------------------ | --------------------------- | ----------- | ----------- |
| `fk_skill__blob__blob`  | `icon_blob_id`              | [`blob.blob`][table:blob.blob]                   | `blob_id`                   | `NO ACTION` | `NO ACTION` |
| `fk_skill__resume`      | `resume_id`                 | [`resume.resume`][table:resume.resume]           | `resume_id`                 | `CASCADE`   | `NO ACTION` |
| `fk_skill__skill_group` | `skill_group_id, resume_id` | [`resume.skill_group`][table:resume.skill_group] | `skill_group_id, resume_id` | `SET NULL`  | `NO ACTION` |

### Incoming foreign keys

| Name                        | Columns    | Table                                                    | Ref Columns | On Delete | On Update   |
| --------------------------- | ---------- | -------------------------------------------------------- | ----------- | --------- | ----------- |
| `fk_skill__tag__tag__skill` | `skill_id` | [`resume.skill__tag__tag`][table:resume.skill__tag__tag] | `skill_id`  | `CASCADE` | `NO ACTION` |

### Diagram

```mermaid
erDiagram

"resume.skill" }o--o| "blob.blob" : "icon_blob_id"
"resume.skill" }o--|| "resume.resume" : "resume_id"
"resume.skill" }o--o| "resume.skill_group" : "skill_group_id, resume_id"
"resume.skill__tag__tag" }o--|| "resume.skill" : "skill_id"

"resume.skill" {
  INTEGER skill_id PK
  INTEGER resume_id FK
  INTEGER skill_group_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
  INTEGER icon_blob_id FK
  text.url url
  temporal.year_optional_month first_used
  temporal.year_optional_month last_used
  text.trimmed versions
  SMALLINT rating
  BOOLEAN is_highlight
  text.trimmed notes
}

"blob.blob" {
  INTEGER blob_id PK
  hash.sha256 sha256 UK
  INTEGER mime_type_id FK
  BIGINT byte_size
  INTEGER width
  INTEGER height
  BYTEA thumbhash
  text.trimmed original_file_name
  TIMESTAMPTZ created_utc
}

"resume.resume" {
  INTEGER resume_id PK
  text.kebab_case slug UK
  text.trimmed title
  text.trimmed private_notes
  BOOLEAN is_default
  BOOLEAN is_published
  TIMESTAMPTZ created_utc
  text.trimmed name
  text.trimmed label
  INTEGER image_blob_id FK
  text.email email
  text.trimmed phone
  text.url url
  text.trimmed summary
  text.trimmed location_address
  text.trimmed location_postal_code
  text.trimmed location_city
  text.trimmed location_region
  TEXT location_country_code
}

"resume.skill_group" {
  INTEGER skill_group_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
}

"resume.skill__tag__tag" {
  INTEGER skill_id PK, FK
  INTEGER tag_id PK, FK
}
```

---

> Generated by [scripts/schema-docs.cs](../../scripts/schema-docs.cs). Do not edit by hand.

[domain:temporal.year_optional_month]: domains.md#temporalyear_optional_month
[domain:text.trimmed]: domains.md#texttrimmed
[domain:text.url]: domains.md#texturl
[pg:boolean]: https://www.postgresql.org/docs/19/datatype-boolean.html
[pg:integer]: https://www.postgresql.org/docs/19/datatype-numeric.html#DATATYPE-INT
[pg:smallint]: https://www.postgresql.org/docs/19/datatype-numeric.html#DATATYPE-INT
[table:blob.blob]: blob.blob.md
[table:resume.resume]: resume.resume.md
[table:resume.skill__tag__tag]: resume.skill__tag__tag.md
[table:resume.skill_group]: resume.skill_group.md
