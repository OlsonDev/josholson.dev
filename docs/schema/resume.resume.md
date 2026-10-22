# `resume.resume`

## Columns

| Name                    | Type                                         |   Nullable | Default                        |
| ----------------------- | -------------------------------------------- | ---------: | ------------------------------ |
| `resume_id`             | [`INTEGER`][pg:integer]                      | `NOT NULL` | `GENERATED ALWAYS AS IDENTITY` |
| `slug`                  | [`text.kebab_case`][domain:text.kebab_case]  | `NOT NULL` |                                |
| `title`                 | [`text.trimmed`][domain:text.trimmed]        | `NOT NULL` |                                |
| `private_notes`         | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `is_default`            | [`BOOLEAN`][pg:boolean]                      | `NOT NULL` | `false`                        |
| `is_published`          | [`BOOLEAN`][pg:boolean]                      | `NOT NULL` | `false`                        |
| `created_utc`           | [`TIMESTAMPTZ`][pg:timestamp-with-time-zone] | `NOT NULL` | `now()`                        |
| `name`                  | [`text.trimmed`][domain:text.trimmed]        | `NOT NULL` |                                |
| `label`                 | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `image_blob_id`         | [`INTEGER`][pg:integer]                      |     `NULL` |                                |
| `email`                 | [`text.email`][domain:text.email]            |     `NULL` |                                |
| `phone`                 | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `url`                   | [`text.url`][domain:text.url]                |     `NULL` |                                |
| `summary`               | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `location_address`      | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `location_postal_code`  | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `location_city`         | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `location_region`       | [`text.trimmed`][domain:text.trimmed]        |     `NULL` |                                |
| `location_country_code` | [`TEXT`][pg:text]                            |     `NULL` |                                |

## Constraints

### Primary key

| Name        | Definition  |
| ----------- | ----------- |
| `pk_resume` | `resume_id` |

### Unique constraints

| Name             | Definition |
| ---------------- | ---------- |
| `uq_resume_slug` | `slug`     |

### Check constraints

| Name                                     | Definition                                   |
| ---------------------------------------- | -------------------------------------------- |
| `ck_resume_default_requires_published`   | `(NOT is_default) OR is_published`           |
| `ck_resume_location_country_code_format` | `location_country_code ~ '^[A-Z]{2}$'::text` |

## Indexes

```sql
CREATE INDEX ix_resume_image_blob_id
  ON resume.resume
  USING btree (image_blob_id)
  WHERE (image_blob_id IS NOT NULL);

CREATE UNIQUE INDEX ux_resume_one_default
  ON resume.resume
  USING btree (is_default)
  WHERE is_default;
```

## Relations

### Foreign keys

| Name                    | Columns         | Ref Table                      | Ref Columns | On Delete   | On Update   |
| ----------------------- | --------------- | ------------------------------ | ----------- | ----------- | ----------- |
| `fk_resume__blob__blob` | `image_blob_id` | [`blob.blob`][table:blob.blob] | `blob_id`   | `NO ACTION` | `NO ACTION` |

### Incoming foreign keys

| Name                     | Columns     | Table                                            | Ref Columns | On Delete | On Update   |
| ------------------------ | ----------- | ------------------------------------------------ | ----------- | --------- | ----------- |
| `fk_award__resume`       | `resume_id` | [`resume.award`][table:resume.award]             | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_certificate__resume` | `resume_id` | [`resume.certificate`][table:resume.certificate] | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_education__resume`   | `resume_id` | [`resume.education`][table:resume.education]     | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_interest__resume`    | `resume_id` | [`resume.interest`][table:resume.interest]       | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_language__resume`    | `resume_id` | [`resume.language`][table:resume.language]       | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_profile__resume`     | `resume_id` | [`resume.profile`][table:resume.profile]         | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_project__resume`     | `resume_id` | [`resume.project`][table:resume.project]         | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_publication__resume` | `resume_id` | [`resume.publication`][table:resume.publication] | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_reference__resume`   | `resume_id` | [`resume.reference`][table:resume.reference]     | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_skill__resume`       | `resume_id` | [`resume.skill`][table:resume.skill]             | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_skill_group__resume` | `resume_id` | [`resume.skill_group`][table:resume.skill_group] | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_volunteer__resume`   | `resume_id` | [`resume.volunteer`][table:resume.volunteer]     | `resume_id` | `CASCADE` | `NO ACTION` |
| `fk_work__resume`        | `resume_id` | [`resume.work`][table:resume.work]               | `resume_id` | `CASCADE` | `NO ACTION` |

### Diagram

```mermaid
erDiagram

"resume.award" }o--|| "resume.resume" : "resume_id"
"resume.certificate" }o--|| "resume.resume" : "resume_id"
"resume.education" }o--|| "resume.resume" : "resume_id"
"resume.interest" }o--|| "resume.resume" : "resume_id"
"resume.language" }o--|| "resume.resume" : "resume_id"
"resume.profile" }o--|| "resume.resume" : "resume_id"
"resume.project" }o--|| "resume.resume" : "resume_id"
"resume.publication" }o--|| "resume.resume" : "resume_id"
"resume.reference" }o--|| "resume.resume" : "resume_id"
"resume.resume" }o--o| "blob.blob" : "image_blob_id"
"resume.skill" }o--|| "resume.resume" : "resume_id"
"resume.skill_group" }o--|| "resume.resume" : "resume_id"
"resume.volunteer" }o--|| "resume.resume" : "resume_id"
"resume.work" }o--|| "resume.resume" : "resume_id"

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

"resume.award" {
  INTEGER award_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed title
  temporal.year_optional_month date_awarded
  text.trimmed awarder
  text.trimmed summary
}

"resume.certificate" {
  INTEGER certificate_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
  temporal.year_optional_month date_issued
  temporal.year_optional_month date_expires
  text.trimmed issuer
  text.trimmed credential_id
  text.url url
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

"resume.interest" {
  INTEGER interest_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
  text.trimmed[] keywords
}

"resume.language" {
  INTEGER language_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed language
  TEXT language_code
  text.trimmed fluency
  SMALLINT proficiency
}

"resume.profile" {
  INTEGER profile_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed network
  text.trimmed username
  text.url url
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

"resume.publication" {
  INTEGER publication_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
  SMALLINT type
  text.trimmed publisher
  temporal.year_optional_month release_date
  TEXT doi
  text.url url
  text.trimmed summary
}

"resume.reference" {
  INTEGER reference_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
  text.trimmed relationship
  text.trimmed position
  text.trimmed organization
  temporal.year_optional_month date_received
  text.trimmed testimonial
  text.email email
  text.trimmed phone
  text.url url
  BOOLEAN is_contact_public
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

"resume.skill_group" {
  INTEGER skill_group_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed name
}

"resume.volunteer" {
  INTEGER volunteer_id PK
  INTEGER resume_id FK
  INTEGER sort_order
  BOOLEAN is_visible
  text.trimmed organization
  text.trimmed position
  text.url url
  text.trimmed summary
  text.trimmed[] highlights
  temporal.year_optional_month start_date
  temporal.year_optional_month end_date
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

[domain:text.email]: domains.md#textemail
[domain:text.kebab_case]: domains.md#textkebab_case
[domain:text.trimmed]: domains.md#texttrimmed
[domain:text.url]: domains.md#texturl
[pg:boolean]: https://www.postgresql.org/docs/19/datatype-boolean.html
[pg:integer]: https://www.postgresql.org/docs/19/datatype-numeric.html#DATATYPE-INT
[pg:text]: https://www.postgresql.org/docs/19/datatype-character.html
[pg:timestamp-with-time-zone]: https://www.postgresql.org/docs/19/datatype-datetime.html
[table:blob.blob]: blob.blob.md
[table:resume.award]: resume.award.md
[table:resume.certificate]: resume.certificate.md
[table:resume.education]: resume.education.md
[table:resume.interest]: resume.interest.md
[table:resume.language]: resume.language.md
[table:resume.profile]: resume.profile.md
[table:resume.project]: resume.project.md
[table:resume.publication]: resume.publication.md
[table:resume.reference]: resume.reference.md
[table:resume.skill]: resume.skill.md
[table:resume.skill_group]: resume.skill_group.md
[table:resume.volunteer]: resume.volunteer.md
[table:resume.work]: resume.work.md
