CREATE SCHEMA resume;

CREATE TABLE resume.resume (
    resume_id              INT              NOT NULL GENERATED ALWAYS AS IDENTITY
  , slug                   text.kebab_case  NOT NULL
  , title                  text.trimmed     NOT NULL
  , private_notes          text.trimmed         NULL
  , is_default             BOOLEAN          NOT NULL DEFAULT FALSE
  , is_published           BOOLEAN          NOT NULL DEFAULT FALSE
  , created_utc            TIMESTAMPTZ      NOT NULL DEFAULT now()
  , name                   text.trimmed     NOT NULL
  , label                  text.trimmed         NULL
  , image_blob_id          INT                  NULL
  , email                  text.email           NULL
  , phone                  text.trimmed         NULL
  , url                    text.url             NULL
  , summary                text.trimmed         NULL
  , location_address       text.trimmed         NULL
  , location_postal_code   text.trimmed         NULL
  , location_city          text.trimmed         NULL
  , location_region        text.trimmed         NULL
  , location_country_code  TEXT                 NULL

  , CONSTRAINT pk_resume PRIMARY KEY (resume_id)
  , CONSTRAINT uq_resume_slug UNIQUE (slug)
  , CONSTRAINT fk_resume__blob__blob FOREIGN KEY (image_blob_id) REFERENCES blob.blob (blob_id)
  , CONSTRAINT ck_resume_location_country_code_format CHECK (location_country_code ~ '^[A-Z]{2}$')
  , CONSTRAINT ck_resume_default_requires_published   CHECK (NOT is_default OR is_published)
);

CREATE UNIQUE INDEX ux_resume_one_default
  ON resume.resume (is_default)
  WHERE is_default;

CREATE INDEX ix_resume_image_blob_id
  ON resume.resume (image_blob_id)
  WHERE image_blob_id IS NOT NULL;

CREATE TABLE resume.profile (
    profile_id  INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id   INT           NOT NULL
  , sort_order  INT           NOT NULL DEFAULT 0
  , is_visible  BOOLEAN       NOT NULL DEFAULT TRUE
  , network     text.trimmed  NOT NULL
  , username    text.trimmed      NULL
  , url         text.url          NULL

  , CONSTRAINT pk_profile PRIMARY KEY (profile_id)
  , CONSTRAINT fk_profile__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
);

CREATE INDEX ix_profile_resume_id_sort_order
  ON resume.profile (resume_id, sort_order);

CREATE TABLE resume.work (
    work_id          INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id        INT                           NOT NULL
  , sort_order       INT                           NOT NULL DEFAULT 0
  , is_visible       BOOLEAN                       NOT NULL DEFAULT TRUE
  , name             text.trimmed                  NOT NULL
  , position         text.trimmed                      NULL
  , employment_type  SMALLINT                          NULL
  , location         text.trimmed                      NULL
  , url              text.url                          NULL
  , logo_blob_id     INT                               NULL
  , description      text.trimmed                      NULL
  , summary          text.trimmed                      NULL
  , highlights       text.trimmed[]                NOT NULL DEFAULT '{}'
  , start_date       temporal.year_optional_month  NOT NULL
  , end_date         temporal.year_optional_month      NULL

  , CONSTRAINT pk_work PRIMARY KEY (work_id)
  , CONSTRAINT fk_work__resume     FOREIGN KEY (resume_id)    REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT fk_work__blob__blob FOREIGN KEY (logo_blob_id) REFERENCES blob.blob (blob_id)
  , CONSTRAINT ck_work_employment_type_range CHECK (employment_type BETWEEN 1 AND 5)
  , CONSTRAINT ck_work_end_not_before_start  CHECK (end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
  , CONSTRAINT ck_work_highlights_no_nulls   CHECK (array_position(highlights, NULL) IS NULL)
);

CREATE INDEX ix_work_resume_id_sort_order
  ON resume.work (resume_id, sort_order);

CREATE INDEX ix_work_logo_blob_id
  ON resume.work (logo_blob_id)
  WHERE logo_blob_id IS NOT NULL;

CREATE TABLE resume.work__tag__tag (
    work_id  INT  NOT NULL
  , tag_id   INT  NOT NULL

  , CONSTRAINT pk_work__tag__tag PRIMARY KEY (work_id, tag_id)
  , CONSTRAINT fk_work__tag__tag__work     FOREIGN KEY (work_id) REFERENCES resume.work (work_id) ON DELETE CASCADE
  , CONSTRAINT fk_work__tag__tag__tag__tag FOREIGN KEY (tag_id)  REFERENCES tag.tag (tag_id)      ON DELETE CASCADE
);

CREATE INDEX ix_work__tag__tag_tag_id_work_id
  ON resume.work__tag__tag (tag_id, work_id);

CREATE TABLE resume.volunteer (
    volunteer_id  INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id     INT                           NOT NULL
  , sort_order    INT                           NOT NULL DEFAULT 0
  , is_visible    BOOLEAN                       NOT NULL DEFAULT TRUE
  , organization  text.trimmed                  NOT NULL
  , position      text.trimmed                      NULL
  , url           text.url                          NULL
  , summary       text.trimmed                      NULL
  , highlights    text.trimmed[]                NOT NULL DEFAULT '{}'
  , start_date    temporal.year_optional_month  NOT NULL
  , end_date      temporal.year_optional_month      NULL

  , CONSTRAINT pk_volunteer PRIMARY KEY (volunteer_id)
  , CONSTRAINT fk_volunteer__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT ck_volunteer_end_not_before_start CHECK (end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
  , CONSTRAINT ck_volunteer_highlights_no_nulls  CHECK (array_position(highlights, NULL) IS NULL)
);

CREATE INDEX ix_volunteer_resume_id_sort_order
  ON resume.volunteer (resume_id, sort_order);

CREATE TABLE resume.education (
    education_id  INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id     INT                           NOT NULL
  , sort_order    INT                           NOT NULL DEFAULT 0
  , is_visible    BOOLEAN                       NOT NULL DEFAULT TRUE
  , institution   text.trimmed                  NOT NULL
  , url           text.url                          NULL
  , logo_blob_id  INT                               NULL
  , area          text.trimmed                      NULL
  , study_type    text.trimmed                      NULL
  , score         text.trimmed                      NULL
  , honors        text.trimmed                      NULL
  , courses       text.trimmed[]                NOT NULL DEFAULT '{}'
  , start_date    temporal.year_optional_month  NOT NULL
  , end_date      temporal.year_optional_month      NULL

  , CONSTRAINT pk_education PRIMARY KEY (education_id)
  , CONSTRAINT fk_education__resume     FOREIGN KEY (resume_id)    REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT fk_education__blob__blob FOREIGN KEY (logo_blob_id) REFERENCES blob.blob (blob_id)
  , CONSTRAINT ck_education_end_not_before_start CHECK (end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
  , CONSTRAINT ck_education_courses_no_nulls     CHECK (array_position(courses, NULL) IS NULL)
);

CREATE INDEX ix_education_resume_id_sort_order
  ON resume.education (resume_id, sort_order);

CREATE INDEX ix_education_logo_blob_id
  ON resume.education (logo_blob_id)
  WHERE logo_blob_id IS NOT NULL;

CREATE TABLE resume.award (
    award_id      INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id     INT                           NOT NULL
  , sort_order    INT                           NOT NULL DEFAULT 0
  , is_visible    BOOLEAN                       NOT NULL DEFAULT TRUE
  , title         text.trimmed                  NOT NULL
  , date_awarded  temporal.year_optional_month      NULL
  , awarder       text.trimmed                      NULL
  , summary       text.trimmed                      NULL

  , CONSTRAINT pk_award PRIMARY KEY (award_id)
  , CONSTRAINT fk_award__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
);

CREATE INDEX ix_award_resume_id_sort_order
  ON resume.award (resume_id, sort_order);

CREATE TABLE resume.certificate (
    certificate_id  INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id       INT                           NOT NULL
  , sort_order      INT                           NOT NULL DEFAULT 0
  , is_visible      BOOLEAN                       NOT NULL DEFAULT TRUE
  , name            text.trimmed                  NOT NULL
  , date_issued     temporal.year_optional_month      NULL
  , date_expires    temporal.year_optional_month      NULL
  , issuer          text.trimmed                      NULL
  , credential_id   text.trimmed                      NULL
  , url             text.url                          NULL

  , CONSTRAINT pk_certificate PRIMARY KEY (certificate_id)
  , CONSTRAINT fk_certificate__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT ck_certificate_expires_not_before_issued CHECK (date_issued IS NULL OR date_expires IS NULL OR left(date_expires, 4) >= left(date_issued, 4))
);

CREATE INDEX ix_certificate_resume_id_sort_order
  ON resume.certificate (resume_id, sort_order);

CREATE TABLE resume.publication (
    publication_id  INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id       INT                           NOT NULL
  , sort_order      INT                           NOT NULL DEFAULT 0
  , is_visible      BOOLEAN                       NOT NULL DEFAULT TRUE
  , name            text.trimmed                  NOT NULL
  , type            SMALLINT                          NULL
  , publisher       text.trimmed                      NULL
  , release_date    temporal.year_optional_month      NULL
  , doi             TEXT                              NULL
  , url             text.url                          NULL
  , summary         text.trimmed                      NULL

  , CONSTRAINT pk_publication PRIMARY KEY (publication_id)
  , CONSTRAINT fk_publication__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT ck_publication_type_range CHECK (type BETWEEN 1 AND 5)
  , CONSTRAINT ck_publication_doi_format CHECK (doi ~ '^10\.\d{4,9}/\S+$')
);

CREATE INDEX ix_publication_resume_id_sort_order
  ON resume.publication (resume_id, sort_order);

CREATE TABLE resume.language (
    language_id    INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id      INT           NOT NULL
  , sort_order     INT           NOT NULL DEFAULT 0
  , is_visible     BOOLEAN       NOT NULL DEFAULT TRUE
  , language       text.trimmed  NOT NULL
  , language_code  TEXT              NULL
  , fluency        text.trimmed      NULL
  , proficiency    SMALLINT          NULL

  , CONSTRAINT pk_language PRIMARY KEY (language_id)
  , CONSTRAINT fk_language__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT ck_language_language_code_format CHECK (language_code ~ '^[a-z]{2,3}(-[A-Za-z0-9]+)*$')
  , CONSTRAINT ck_language_proficiency_range    CHECK (proficiency BETWEEN 1 AND 7)
);

CREATE INDEX ix_language_resume_id_sort_order
  ON resume.language (resume_id, sort_order);

CREATE TABLE resume.interest (
    interest_id  INT             NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id    INT             NOT NULL
  , sort_order   INT             NOT NULL DEFAULT 0
  , is_visible   BOOLEAN         NOT NULL DEFAULT TRUE
  , name         text.trimmed    NOT NULL
  , keywords     text.trimmed[]  NOT NULL DEFAULT '{}'

  , CONSTRAINT pk_interest PRIMARY KEY (interest_id)
  , CONSTRAINT fk_interest__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT ck_interest_keywords_no_nulls CHECK (array_position(keywords, NULL) IS NULL)
);

CREATE INDEX ix_interest_resume_id_sort_order
  ON resume.interest (resume_id, sort_order);

CREATE TABLE resume.reference (
    reference_id       INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id          INT                           NOT NULL
  , sort_order         INT                           NOT NULL DEFAULT 0
  , is_visible         BOOLEAN                       NOT NULL DEFAULT TRUE
  , name               text.trimmed                  NOT NULL
  , relationship       text.trimmed                      NULL
  , position           text.trimmed                      NULL
  , organization       text.trimmed                      NULL
  , date_received      temporal.year_optional_month      NULL
  , testimonial        text.trimmed                      NULL
  , email              text.email                        NULL
  , phone              text.trimmed                      NULL
  , url                text.url                          NULL
  , is_contact_public  BOOLEAN                       NOT NULL DEFAULT FALSE

  , CONSTRAINT pk_reference PRIMARY KEY (reference_id)
  , CONSTRAINT fk_reference__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
);

CREATE INDEX ix_reference_resume_id_sort_order
  ON resume.reference (resume_id, sort_order);

CREATE TABLE resume.project (
    project_id      INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id       INT                           NOT NULL
  , sort_order      INT                           NOT NULL DEFAULT 0
  , is_visible      BOOLEAN                       NOT NULL DEFAULT TRUE
  , slug            text.kebab_case               NOT NULL
  , name            text.trimmed                  NOT NULL
  , description     text.trimmed                      NULL
  , highlights      text.trimmed[]                NOT NULL DEFAULT '{}'
  , body            text.trimmed                      NULL
  , cover_blob_id   INT                               NULL
  , url             text.url                          NULL
  , repository_url  text.url                          NULL
  , demo_url        text.url                          NULL
  , roles           text.trimmed[]                NOT NULL DEFAULT '{}'
  , entity          text.trimmed                      NULL
  , type            text.trimmed                      NULL
  , status          SMALLINT                      NOT NULL DEFAULT 1
  , is_featured     BOOLEAN                       NOT NULL DEFAULT FALSE
  , start_date      temporal.year_optional_month      NULL
  , end_date        temporal.year_optional_month      NULL

  , CONSTRAINT pk_project PRIMARY KEY (project_id)
  , CONSTRAINT uq_project_resume_slug UNIQUE (resume_id, slug)
  , CONSTRAINT fk_project__resume     FOREIGN KEY (resume_id)     REFERENCES resume.resume (resume_id) ON DELETE CASCADE
  , CONSTRAINT fk_project__blob__blob FOREIGN KEY (cover_blob_id) REFERENCES blob.blob (blob_id)
  , CONSTRAINT ck_project_status_range         CHECK (status BETWEEN 1 AND 3)
  , CONSTRAINT ck_project_end_not_before_start CHECK (start_date IS NULL OR end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
  , CONSTRAINT ck_project_highlights_no_nulls  CHECK (array_position(highlights, NULL) IS NULL)
  , CONSTRAINT ck_project_roles_no_nulls       CHECK (array_position(roles, NULL) IS NULL)
);

CREATE INDEX ix_project_resume_id_sort_order
  ON resume.project (resume_id, sort_order);

CREATE INDEX ix_project_cover_blob_id
  ON resume.project (cover_blob_id)
  WHERE cover_blob_id IS NOT NULL;

CREATE TABLE resume.project__tag__tag (
    project_id  INT  NOT NULL
  , tag_id      INT  NOT NULL

  , CONSTRAINT pk_project__tag__tag PRIMARY KEY (project_id, tag_id)
  , CONSTRAINT fk_project__tag__tag__project  FOREIGN KEY (project_id) REFERENCES resume.project (project_id) ON DELETE CASCADE
  , CONSTRAINT fk_project__tag__tag__tag__tag FOREIGN KEY (tag_id)     REFERENCES tag.tag (tag_id)            ON DELETE CASCADE
);

CREATE INDEX ix_project__tag__tag_tag_id_project_id
  ON resume.project__tag__tag (tag_id, project_id);

CREATE TABLE resume.skill_group (
    skill_group_id  INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id       INT           NOT NULL
  , sort_order      INT           NOT NULL DEFAULT 0
  , is_visible      BOOLEAN       NOT NULL DEFAULT TRUE
  , name            text.trimmed  NOT NULL

  , CONSTRAINT pk_skill_group PRIMARY KEY (skill_group_id)
  , CONSTRAINT uq_skill_group_resume_name UNIQUE (resume_id, name)
  , CONSTRAINT uq_skill_group_skill_group_id_resume_id UNIQUE (skill_group_id, resume_id)
  , CONSTRAINT fk_skill_group__resume FOREIGN KEY (resume_id) REFERENCES resume.resume (resume_id) ON DELETE CASCADE
);

CREATE INDEX ix_skill_group_resume_id_sort_order
  ON resume.skill_group (resume_id, sort_order);

CREATE TABLE resume.skill (
    skill_id        INT                           NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id       INT                           NOT NULL
  , skill_group_id  INT                               NULL
  , sort_order      INT                           NOT NULL DEFAULT 0
  , is_visible      BOOLEAN                       NOT NULL DEFAULT TRUE
  , name            text.trimmed                  NOT NULL
  , icon_blob_id    INT                               NULL
  , url             text.url                          NULL
  , first_used      temporal.year_optional_month      NULL
  , last_used       temporal.year_optional_month      NULL
  , versions        text.trimmed                      NULL
  , rating          SMALLINT                          NULL
  , is_highlight    BOOLEAN                       NOT NULL DEFAULT FALSE
  , notes           text.trimmed                      NULL

  , CONSTRAINT pk_skill PRIMARY KEY (skill_id)
  , CONSTRAINT uq_skill_resume_name UNIQUE (resume_id, name)
  , CONSTRAINT fk_skill__resume      FOREIGN KEY (resume_id)                 REFERENCES resume.resume (resume_id)                      ON DELETE CASCADE
  , CONSTRAINT fk_skill__skill_group FOREIGN KEY (skill_group_id, resume_id) REFERENCES resume.skill_group (skill_group_id, resume_id) ON DELETE SET NULL (skill_group_id)
  , CONSTRAINT fk_skill__blob__blob  FOREIGN KEY (icon_blob_id)              REFERENCES blob.blob (blob_id)
  , CONSTRAINT ck_skill_rating_range                    CHECK (rating BETWEEN 0 AND 10)
  , CONSTRAINT ck_skill_last_used_not_before_first_used CHECK (first_used IS NULL OR last_used IS NULL OR left(last_used, 4) >= left(first_used, 4))
);

CREATE INDEX ix_skill_resume_id_sort_order
  ON resume.skill (resume_id, sort_order);

CREATE INDEX ix_skill_skill_group_id
  ON resume.skill (skill_group_id)
  WHERE skill_group_id IS NOT NULL;

CREATE INDEX ix_skill_icon_blob_id
  ON resume.skill (icon_blob_id)
  WHERE icon_blob_id IS NOT NULL;

CREATE TABLE resume.skill__tag__tag (
    skill_id  INT  NOT NULL
  , tag_id    INT  NOT NULL

  , CONSTRAINT pk_skill__tag__tag PRIMARY KEY (skill_id, tag_id)
  , CONSTRAINT fk_skill__tag__tag__skill    FOREIGN KEY (skill_id) REFERENCES resume.skill (skill_id) ON DELETE CASCADE
  , CONSTRAINT fk_skill__tag__tag__tag__tag FOREIGN KEY (tag_id)   REFERENCES tag.tag (tag_id)        ON DELETE CASCADE
);

CREATE INDEX ix_skill__tag__tag_tag_id_skill_id
  ON resume.skill__tag__tag (tag_id, skill_id);
