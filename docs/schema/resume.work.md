# `resume.work`

## Columns

| Name              | Type                                                                  |   Nullable | Default                        |
| ----------------- | --------------------------------------------------------------------- | ---------: | ------------------------------ |
| `work_id`         | [`INTEGER`][pg:integer]                                               | `NOT NULL` | `GENERATED ALWAYS AS IDENTITY` |
| `resume_id`       | [`INTEGER`][pg:integer]                                               | `NOT NULL` |                                |
| `sort_order`      | [`INTEGER`][pg:integer]                                               | `NOT NULL` | `0`                            |
| `is_visible`      | [`BOOLEAN`][pg:boolean]                                               | `NOT NULL` | `true`                         |
| `name`            | [`text.trimmed`][domain:text.trimmed]                                 | `NOT NULL` |                                |
| `position`        | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `employment_type` | [`SMALLINT`][pg:smallint]                                             |     `NULL` |                                |
| `location`        | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `url`             | [`text.url`][domain:text.url]                                         |     `NULL` |                                |
| `logo_blob_id`    | [`INTEGER`][pg:integer]                                               |     `NULL` |                                |
| `description`     | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `summary`         | [`text.trimmed`][domain:text.trimmed]                                 |     `NULL` |                                |
| `highlights`      | [`text.trimmed[]`][domain:text.trimmed]                               | `NOT NULL` | `'{}'::text.trimmed[]`         |
| `start_date`      | [`temporal.year_optional_month`][domain:temporal.year_optional_month] | `NOT NULL` |                                |
| `end_date`        | [`temporal.year_optional_month`][domain:temporal.year_optional_month] |     `NULL` |                                |

## Constraints

### Primary key

| Name      | Definition |
| --------- | ---------- |
| `pk_work` | `work_id`  |

### Check constraints

| Name                            | Definition                                                                             |
| ------------------------------- | -------------------------------------------------------------------------------------- |
| `ck_work_employment_type_range` | `(employment_type >= 1) AND (employment_type <= 5)`                                    |
| `ck_work_end_not_before_start`  | `(end_date IS NULL) OR ("left"((end_date)::text, 4) >= "left"((start_date)::text, 4))` |
| `ck_work_highlights_no_nulls`   | `array_position(highlights, (NULL::text)::text.trimmed) IS NULL`                       |

## Indexes

```sql
CREATE INDEX ix_work_logo_blob_id
  ON resume.work
  USING btree (logo_blob_id)
  WHERE (logo_blob_id IS NOT NULL);

CREATE INDEX ix_work_resume_id_sort_order
  ON resume.work
  USING btree (resume_id, sort_order);
```

## Relations

### Foreign keys

| Name                  | Columns        | Ref Table                              | Ref Columns | On Delete   | On Update   |
| --------------------- | -------------- | -------------------------------------- | ----------- | ----------- | ----------- |
| `fk_work__blob__blob` | `logo_blob_id` | [`blob.blob`][table:blob.blob]         | `blob_id`   | `NO ACTION` | `NO ACTION` |
| `fk_work__resume`     | `resume_id`    | [`resume.resume`][table:resume.resume] | `resume_id` | `CASCADE`   | `NO ACTION` |

### Incoming foreign keys

| Name                      | Columns   | Table                                                  | Ref Columns | On Delete | On Update   |
| ------------------------- | --------- | ------------------------------------------------------ | ----------- | --------- | ----------- |
| `fk_work__tag__tag__work` | `work_id` | [`resume.work__tag__tag`][table:resume.work__tag__tag] | `work_id`   | `CASCADE` | `NO ACTION` |

### Diagram

```mermaid
erDiagram

"resume.work" }o--o| "blob.blob" : "logo_blob_id"
"resume.work" }o--|| "resume.resume" : "resume_id"
"resume.work__tag__tag" }o--|| "resume.work" : "work_id"

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

"resume.work__tag__tag" {
  INTEGER work_id PK, FK
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
[table:resume.work__tag__tag]: resume.work__tag__tag.md
