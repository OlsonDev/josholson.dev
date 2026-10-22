# `blob.blob`

## Columns

| Name                 | Type                                         |   Nullable | Default                        |
| -------------------- | -------------------------------------------- | ---------: | ------------------------------ |
| `blob_id`            | [`INTEGER`][pg:integer]                      | `NOT NULL` | `GENERATED ALWAYS AS IDENTITY` |
| `sha256`             | [`hash.sha256`][domain:hash.sha256]          | `NOT NULL` |                                |
| `mime_type_id`       | [`INTEGER`][pg:integer]                      | `NOT NULL` |                                |
| `byte_size`          | [`BIGINT`][pg:bigint]                        | `NOT NULL` |                                |
| `width`              | [`INTEGER`][pg:integer]                      |     `NULL` |                                |
| `height`             | [`INTEGER`][pg:integer]                      |     `NULL` |                                |
| `thumbhash`          | [`BYTEA`][pg:bytea]                          |     `NULL` |                                |
| `original_file_name` | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `created_utc`        | [`TIMESTAMPTZ`][pg:timestamp-with-time-zone] | `NOT NULL` | `now()`                        |

## Constraints

### Primary key

| Name      | Definition |
| --------- | ---------- |
| `pk_blob` | `blob_id`  |

### Unique constraints

| Name             | Definition |
| ---------------- | ---------- |
| `uq_blob_sha256` | `sha256`   |

### Check constraints

| Name                             | Definition                           |
| -------------------------------- | ------------------------------------ |
| `ck_blob_byte_size_non_negative` | `byte_size >= 0`                     |
| `ck_blob_dimensions_together`    | `(width IS NULL) = (height IS NULL)` |
| `ck_blob_height_positive`        | `height > 0`                         |
| `ck_blob_width_positive`         | `width > 0`                          |

## Indexes

```sql
CREATE INDEX ix_blob_mime_type_id
  ON blob.blob
  USING btree (mime_type_id);
```

## Relations

### Foreign keys

| Name                 | Columns        | Ref Table                                | Ref Columns    | On Delete   | On Update   |
| -------------------- | -------------- | ---------------------------------------- | -------------- | ----------- | ----------- |
| `fk_blob__mime_type` | `mime_type_id` | [`blob.mime_type`][table:blob.mime_type] | `mime_type_id` | `NO ACTION` | `NO ACTION` |

### Incoming foreign keys

| Name                       | Columns         | Table                                        | Ref Columns | On Delete   | On Update   |
| -------------------------- | --------------- | -------------------------------------------- | ----------- | ----------- | ----------- |
| `fk_post__blob__blob`      | `cover_blob_id` | [`blog.post`][table:blog.post]               | `blob_id`   | `NO ACTION` | `NO ACTION` |
| `fk_education__blob__blob` | `logo_blob_id`  | [`resume.education`][table:resume.education] | `blob_id`   | `NO ACTION` | `NO ACTION` |
| `fk_project__blob__blob`   | `cover_blob_id` | [`resume.project`][table:resume.project]     | `blob_id`   | `NO ACTION` | `NO ACTION` |
| `fk_resume__blob__blob`    | `image_blob_id` | [`resume.resume`][table:resume.resume]       | `blob_id`   | `NO ACTION` | `NO ACTION` |
| `fk_skill__blob__blob`     | `icon_blob_id`  | [`resume.skill`][table:resume.skill]         | `blob_id`   | `NO ACTION` | `NO ACTION` |
| `fk_work__blob__blob`      | `logo_blob_id`  | [`resume.work`][table:resume.work]           | `blob_id`   | `NO ACTION` | `NO ACTION` |

### Diagram

```mermaid
erDiagram

"blob.blob" }o--|| "blob.mime_type" : "mime_type_id"
"blog.post" }o--o| "blob.blob" : "cover_blob_id"
"resume.education" }o--o| "blob.blob" : "logo_blob_id"
"resume.project" }o--o| "blob.blob" : "cover_blob_id"
"resume.resume" }o--o| "blob.blob" : "image_blob_id"
"resume.skill" }o--o| "blob.blob" : "icon_blob_id"
"resume.work" }o--o| "blob.blob" : "logo_blob_id"

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

"blob.mime_type" {
  INTEGER mime_type_id PK
  text.trimmed name UK
  BOOLEAN is_image
}

"blog.post" {
  INTEGER post_id PK
  text.kebab_case slug UK
  text.trimmed title
  text.trimmed summary
  text.trimmed body
  INTEGER cover_blob_id FK
  INTEGER series_id FK
  INTEGER series_order
  text.url canonical_url
  BOOLEAN is_featured
  TIMESTAMPTZ published_utc
  TIMESTAMPTZ updated_utc
  TIMESTAMPTZ created_utc
}

"resume.education" {
  INTEGER education_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed institution
  text.url url
  INTEGER logo_blob_id FK
  text.trimmed area
  text.trimmed study_type
  text.trimmed score
  text.trimmed honors
  text.trimmed[] courses
  temporal.year_optional_month start_date
  temporal.year_optional_month end_date
}

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

"resume.work" {
  INTEGER work_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
  text.trimmed position
  SMALLINT employment_type
  text.trimmed location
  text.url url
  INTEGER logo_blob_id FK
  text.trimmed description
  text.trimmed summary
  text.trimmed[] highlights
  temporal.year_optional_month start_date
  temporal.year_optional_month end_date
}
```

---

> Generated by [scripts/schema-docs.cs](../../scripts/schema-docs.cs). Do not edit by hand.

[domain:hash.sha256]: domains.md#hashsha256
[domain:text.trimmed]: domains.md#texttrimmed
[pg:bigint]: https://www.postgresql.org/docs/19/datatype-numeric.html#DATATYPE-INT
[pg:bytea]: https://www.postgresql.org/docs/19/datatype-binary.html
[pg:integer]: https://www.postgresql.org/docs/19/datatype-numeric.html#DATATYPE-INT
[pg:timestamp-with-time-zone]: https://www.postgresql.org/docs/19/datatype-datetime.html
[table:blob.mime_type]: blob.mime_type.md
[table:blog.post]: blog.post.md
[table:resume.education]: resume.education.md
[table:resume.project]: resume.project.md
[table:resume.resume]: resume.resume.md
[table:resume.skill]: resume.skill.md
[table:resume.work]: resume.work.md
