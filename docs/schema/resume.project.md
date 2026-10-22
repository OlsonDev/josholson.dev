# `resume.project`

## Columns

| Name             | Type                                                                  |   Nullable | Default                        |
| ---------------- | --------------------------------------------------------------------- | ---------: | ------------------------------ |
| `project_id`     | [`INTEGER`][pg:integer]                                               | `NOT NULL` | `GENERATED ALWAYS AS IDENTITY` |
| `resume_id`      | [`INTEGER`][pg:integer]                                               | `NOT NULL` |                                |
| `sort_order`     | [`INTEGER`][pg:integer]                                               | `NOT NULL` | `0`                            |
| `is_visible`     | [`BOOLEAN`][pg:boolean]                                               | `NOT NULL` | `true`                         |
| `slug`           | [`text.kebab_case`][domain:text.kebab_case]                           | `NOT NULL` |                                |
| `name`           | [`text.trimmed`][domain:text.trimmed]                                 | `NOT NULL` |                                |
| `description`    | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `highlights`     | [`text.trimmed[]`][domain:text.trimmed]                               | `NOT NULL` | `'{}'::text.trimmed[]`         |
| `body`           | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `cover_blob_id`  | [`INTEGER`][pg:integer]                                               |     `NULL` |                                |
| `url`            | [`text.url`][domain:text.url]                                         |     `NULL` |                                |
| `repository_url` | [`text.url`][domain:text.url]                                         |     `NULL` |                                |
| `demo_url`       | [`text.url`][domain:text.url]                                         |     `NULL` |                                |
| `roles`          | [`text.trimmed[]`][domain:text.trimmed]                               | `NOT NULL` | `'{}'::text.trimmed[]`         |
| `entity`         | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `type`           | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `status`         | [`SMALLINT`][pg:smallint]                                             | `NOT NULL` | `1`                            |
| `is_featured`    | [`BOOLEAN`][pg:boolean]                                               | `NOT NULL` | `false`                        |
| `start_date`     | [`temporal.year_optional_month`][domain:temporal.year_optional_month] |     `NULL` |                                |
| `end_date`       | [`temporal.year_optional_month`][domain:temporal.year_optional_month] |     `NULL` |                                |

## Constraints

### Primary key

| Name         | Definition   |
| ------------ | ------------ |
| `pk_project` | `project_id` |

### Unique constraints

| Name                     | Definition        |
| ------------------------ | ----------------- |
| `uq_project_resume_slug` | `resume_id, slug` |

### Check constraints

| Name                              | Definition                                                                                                     |
| --------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `ck_project_end_not_before_start` | `(start_date IS NULL) OR (end_date IS NULL) OR ("left"((end_date)::text, 4) >= "left"((start_date)::text, 4))` |
| `ck_project_highlights_no_nulls`  | `array_position(highlights, (NULL::text)::text.trimmed) IS NULL`                                               |
| `ck_project_roles_no_nulls`       | `array_position(roles, (NULL::text)::text.trimmed) IS NULL`                                                    |
| `ck_project_status_range`         | `(status >= 1) AND (status <= 3)`                                                                              |

## Indexes

```sql
CREATE INDEX ix_project_cover_blob_id
  ON resume.project
  USING btree (cover_blob_id)
  WHERE (cover_blob_id IS NOT NULL);

CREATE INDEX ix_project_resume_id_sort_order
  ON resume.project
  USING btree (resume_id, sort_order);
```

## Relations

### Foreign keys

| Name                     | Columns         | Ref Table                              | Ref Columns | On Delete   | On Update   |
| ------------------------ | --------------- | -------------------------------------- | ----------- | ----------- | ----------- |
| `fk_project__blob__blob` | `cover_blob_id` | [`blob.blob`][table:blob.blob]         | `blob_id`   | `NO ACTION` | `NO ACTION` |
| `fk_project__resume`     | `resume_id`     | [`resume.resume`][table:resume.resume] | `resume_id` | `CASCADE`   | `NO ACTION` |

### Incoming foreign keys

| Name                            | Columns      | Table                                                        | Ref Columns  | On Delete | On Update   |
| ------------------------------- | ------------ | ------------------------------------------------------------ | ------------ | --------- | ----------- |
| `fk_project__tag__tag__project` | `project_id` | [`resume.project__tag__tag`][table:resume.project__tag__tag] | `project_id` | `CASCADE` | `NO ACTION` |

### Diagram

```mermaid
erDiagram

"resume.project" }o--o| "blob.blob" : "cover_blob_id"
"resume.project" }o--|| "resume.resume" : "resume_id"
"resume.project__tag__tag" }o--|| "resume.project" : "project_id"

"resume.project" {
  INTEGER project_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.kebab_case slug
  text.trimmed name
  text.trimmed description
  text.trimmed[] highlights
  text.trimmed body
  INTEGER cover_blob_id FK
  text.url url
  text.url repository_url
  text.url demo_url
  text.trimmed[] roles
  text.trimmed entity
  text.trimmed type
  SMALLINT status
  BOOLEAN is_featured
  temporal.year_optional_month start_date
  temporal.year_optional_month end_date
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

"resume.project__tag__tag" {
  INTEGER project_id PK, FK
  INTEGER tag_id PK, FK
}
```

---

> Generated by [scripts/schema-docs.cs](../../scripts/schema-docs.cs). Do not edit by hand.

[domain:temporal.year_optional_month]: domains.md#temporalyear_optional_month
[domain:text.kebab_case]: domains.md#textkebab_case
[domain:text.trimmed]: domains.md#texttrimmed
[domain:text.url]: domains.md#texturl
[pg:boolean]: https://www.postgresql.org/docs/19/datatype-boolean.html
[pg:integer]: https://www.postgresql.org/docs/19/datatype-numeric.html#DATATYPE-INT
[pg:smallint]: https://www.postgresql.org/docs/19/datatype-numeric.html#DATATYPE-INT
[table:blob.blob]: blob.blob.md
[table:resume.project__tag__tag]: resume.project__tag__tag.md
[table:resume.resume]: resume.resume.md
