CREATE SCHEMA resume;

CREATE TABLE resume.resume (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , slug TEXT NOT NULL
  , title TEXT NOT NULL
  , private_notes TEXT NULL
  , is_default BOOLEAN NOT NULL DEFAULT FALSE
  , is_published BOOLEAN NOT NULL DEFAULT FALSE
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , name TEXT NOT NULL
  , label TEXT NULL
  , image_blob_id INT NULL
  , email TEXT NULL
  , phone TEXT NULL
  , url TEXT NULL
  , summary TEXT NULL
  , location_address TEXT NULL
  , location_postal_code TEXT NULL
  , location_city TEXT NULL
  , location_region TEXT NULL
  , location_country_code TEXT NULL
  , CONSTRAINT pk_resume PRIMARY KEY (id)
  , CONSTRAINT uq_resume_slug UNIQUE (slug)
  , CONSTRAINT fk_resume_image_blob FOREIGN KEY (image_blob_id)
      REFERENCES blob.blob (id)
  , CONSTRAINT ck_resume_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
  , CONSTRAINT ck_resume_title_not_empty CHECK (title != '')
  , CONSTRAINT ck_resume_name_not_empty CHECK (name != '')
  , CONSTRAINT ck_resume_location_country_code_format CHECK (location_country_code ~ '^[A-Z]{2}$')
  , CONSTRAINT ck_resume_default_requires_published CHECK (NOT is_default OR is_published)
);

CREATE UNIQUE INDEX ux_resume_one_default ON resume.resume (is_default) WHERE is_default;

CREATE INDEX ix_resume_image_blob_id ON resume.resume (image_blob_id) WHERE image_blob_id IS NOT NULL;

CREATE TABLE resume.profile (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , network TEXT NOT NULL
  , username TEXT NULL
  , url TEXT NULL
  , CONSTRAINT pk_profile PRIMARY KEY (id)
  , CONSTRAINT fk_profile_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_profile_network_not_empty CHECK (network != '')
);

CREATE INDEX ix_profile_resume_id_sort_order ON resume.profile (resume_id, sort_order);

CREATE TABLE resume.work (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , name TEXT NOT NULL
  , position TEXT NULL
  , employment_type SMALLINT NULL
  , location TEXT NULL
  , url TEXT NULL
  , logo_blob_id INT NULL
  , description TEXT NULL
  , summary TEXT NULL
  , highlights TEXT[] NOT NULL DEFAULT '{}'
  , start_date temporal.year_optional_month NOT NULL
  , end_date temporal.year_optional_month NULL
  , CONSTRAINT pk_work PRIMARY KEY (id)
  , CONSTRAINT fk_work_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT fk_work_logo_blob FOREIGN KEY (logo_blob_id)
      REFERENCES blob.blob (id)
  , CONSTRAINT ck_work_name_not_empty CHECK (name != '')
  , CONSTRAINT ck_work_employment_type_range CHECK (employment_type BETWEEN 1 AND 5)
  , CONSTRAINT ck_work_end_not_before_start CHECK (end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
);

CREATE INDEX ix_work_resume_id_sort_order ON resume.work (resume_id, sort_order);

CREATE INDEX ix_work_logo_blob_id ON resume.work (logo_blob_id) WHERE logo_blob_id IS NOT NULL;

CREATE TABLE resume.work_tag (
    work_id INT NOT NULL
  , tag_id INT NOT NULL
  , CONSTRAINT pk_work_tag PRIMARY KEY (work_id, tag_id)
  , CONSTRAINT fk_work_tag_work FOREIGN KEY (work_id)
      REFERENCES resume.work (id) ON DELETE CASCADE
  , CONSTRAINT fk_work_tag_tag FOREIGN KEY (tag_id)
      REFERENCES tag.tag (id) ON DELETE CASCADE
);

CREATE INDEX ix_work_tag_tag_id_work_id ON resume.work_tag (tag_id, work_id);

CREATE TABLE resume.volunteer (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , organization TEXT NOT NULL
  , position TEXT NULL
  , url TEXT NULL
  , summary TEXT NULL
  , highlights TEXT[] NOT NULL DEFAULT '{}'
  , start_date temporal.year_optional_month NOT NULL
  , end_date temporal.year_optional_month NULL
  , CONSTRAINT pk_volunteer PRIMARY KEY (id)
  , CONSTRAINT fk_volunteer_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_volunteer_organization_not_empty CHECK (organization != '')
  , CONSTRAINT ck_volunteer_end_not_before_start CHECK (end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
);

CREATE INDEX ix_volunteer_resume_id_sort_order ON resume.volunteer (resume_id, sort_order);

CREATE TABLE resume.education (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , institution TEXT NOT NULL
  , url TEXT NULL
  , logo_blob_id INT NULL
  , area TEXT NULL
  , study_type TEXT NULL
  , score TEXT NULL
  , honors TEXT NULL
  , courses TEXT[] NOT NULL DEFAULT '{}'
  , start_date temporal.year_optional_month NOT NULL
  , end_date temporal.year_optional_month NULL
  , CONSTRAINT pk_education PRIMARY KEY (id)
  , CONSTRAINT fk_education_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT fk_education_logo_blob FOREIGN KEY (logo_blob_id)
      REFERENCES blob.blob (id)
  , CONSTRAINT ck_education_institution_not_empty CHECK (institution != '')
  , CONSTRAINT ck_education_end_not_before_start CHECK (end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
);

CREATE INDEX ix_education_resume_id_sort_order ON resume.education (resume_id, sort_order);

CREATE INDEX ix_education_logo_blob_id ON resume.education (logo_blob_id) WHERE logo_blob_id IS NOT NULL;

CREATE TABLE resume.award (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , title TEXT NOT NULL
  , date_awarded temporal.year_optional_month NULL
  , awarder TEXT NULL
  , summary TEXT NULL
  , CONSTRAINT pk_award PRIMARY KEY (id)
  , CONSTRAINT fk_award_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_award_title_not_empty CHECK (title != '')
);

CREATE INDEX ix_award_resume_id_sort_order ON resume.award (resume_id, sort_order);

CREATE TABLE resume.certificate (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , name TEXT NOT NULL
  , date_issued temporal.year_optional_month NULL
  , date_expires temporal.year_optional_month NULL
  , issuer TEXT NULL
  , credential_id TEXT NULL
  , url TEXT NULL
  , CONSTRAINT pk_certificate PRIMARY KEY (id)
  , CONSTRAINT fk_certificate_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_certificate_name_not_empty CHECK (name != '')
  , CONSTRAINT ck_certificate_expires_not_before_issued CHECK (date_issued IS NULL OR date_expires IS NULL OR left(date_expires, 4) >= left(date_issued, 4))
);

CREATE INDEX ix_certificate_resume_id_sort_order ON resume.certificate (resume_id, sort_order);

CREATE TABLE resume.publication (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , name TEXT NOT NULL
  , type SMALLINT NULL
  , publisher TEXT NULL
  , release_date temporal.year_optional_month NULL
  , doi TEXT NULL
  , url TEXT NULL
  , summary TEXT NULL
  , CONSTRAINT pk_publication PRIMARY KEY (id)
  , CONSTRAINT fk_publication_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_publication_name_not_empty CHECK (name != '')
  , CONSTRAINT ck_publication_type_range CHECK (type BETWEEN 1 AND 5)
  , CONSTRAINT ck_publication_doi_format CHECK (doi ~ '^10\.\d{4,9}/\S+$')
);

CREATE INDEX ix_publication_resume_id_sort_order ON resume.publication (resume_id, sort_order);

CREATE TABLE resume.language (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , language TEXT NOT NULL
  , language_code TEXT NULL
  , fluency TEXT NULL
  , proficiency SMALLINT NULL
  , CONSTRAINT pk_language PRIMARY KEY (id)
  , CONSTRAINT fk_language_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_language_language_not_empty CHECK (language != '')
  , CONSTRAINT ck_language_language_code_format CHECK (language_code ~ '^[a-z]{2,3}(-[A-Za-z0-9]+)*$')
  , CONSTRAINT ck_language_proficiency_range CHECK (proficiency BETWEEN 1 AND 7)
);

CREATE INDEX ix_language_resume_id_sort_order ON resume.language (resume_id, sort_order);

CREATE TABLE resume.interest (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , name TEXT NOT NULL
  , keywords TEXT[] NOT NULL DEFAULT '{}'
  , CONSTRAINT pk_interest PRIMARY KEY (id)
  , CONSTRAINT fk_interest_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_interest_name_not_empty CHECK (name != '')
);

CREATE INDEX ix_interest_resume_id_sort_order ON resume.interest (resume_id, sort_order);

CREATE TABLE resume.reference (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , name TEXT NOT NULL
  , relationship TEXT NULL
  , position TEXT NULL
  , organization TEXT NULL
  , date_received temporal.year_optional_month NULL
  , testimonial TEXT NULL
  , email TEXT NULL
  , phone TEXT NULL
  , url TEXT NULL
  , is_contact_public BOOLEAN NOT NULL DEFAULT FALSE
  , CONSTRAINT pk_reference PRIMARY KEY (id)
  , CONSTRAINT fk_reference_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_reference_name_not_empty CHECK (name != '')
);

CREATE INDEX ix_reference_resume_id_sort_order ON resume.reference (resume_id, sort_order);

CREATE TABLE resume.project (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , slug TEXT NOT NULL
  , name TEXT NOT NULL
  , description TEXT NULL
  , highlights TEXT[] NOT NULL DEFAULT '{}'
  , body TEXT NULL
  , cover_blob_id INT NULL
  , url TEXT NULL
  , repository_url TEXT NULL
  , demo_url TEXT NULL
  , roles TEXT[] NOT NULL DEFAULT '{}'
  , entity TEXT NULL
  , type TEXT NULL
  , status SMALLINT NOT NULL DEFAULT 1
  , is_featured BOOLEAN NOT NULL DEFAULT FALSE
  , start_date temporal.year_optional_month NULL
  , end_date temporal.year_optional_month NULL
  , CONSTRAINT pk_project PRIMARY KEY (id)
  , CONSTRAINT uq_project_resume_slug UNIQUE (resume_id, slug)
  , CONSTRAINT fk_project_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT fk_project_cover_blob FOREIGN KEY (cover_blob_id)
      REFERENCES blob.blob (id)
  , CONSTRAINT ck_project_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
  , CONSTRAINT ck_project_name_not_empty CHECK (name != '')
  , CONSTRAINT ck_project_status_range CHECK (status BETWEEN 1 AND 3)
  , CONSTRAINT ck_project_end_not_before_start CHECK (start_date IS NULL OR end_date IS NULL OR left(end_date, 4) >= left(start_date, 4))
);

CREATE INDEX ix_project_resume_id_sort_order ON resume.project (resume_id, sort_order);

CREATE INDEX ix_project_cover_blob_id ON resume.project (cover_blob_id) WHERE cover_blob_id IS NOT NULL;

CREATE TABLE resume.project_tag (
    project_id INT NOT NULL
  , tag_id INT NOT NULL
  , CONSTRAINT pk_project_tag PRIMARY KEY (project_id, tag_id)
  , CONSTRAINT fk_project_tag_project FOREIGN KEY (project_id)
      REFERENCES resume.project (id) ON DELETE CASCADE
  , CONSTRAINT fk_project_tag_tag FOREIGN KEY (tag_id)
      REFERENCES tag.tag (id) ON DELETE CASCADE
);

CREATE INDEX ix_project_tag_tag_id_project_id ON resume.project_tag (tag_id, project_id);

CREATE TABLE resume.skill_group (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , name TEXT NOT NULL
  , CONSTRAINT pk_skill_group PRIMARY KEY (id)
  , CONSTRAINT uq_skill_group_resume_name UNIQUE (resume_id, name)
  , CONSTRAINT uq_skill_group_id_resume UNIQUE (id, resume_id)
  , CONSTRAINT fk_skill_group_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT ck_skill_group_name_not_empty CHECK (name != '')
);

CREATE INDEX ix_skill_group_resume_id_sort_order ON resume.skill_group (resume_id, sort_order);

CREATE TABLE resume.skill (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , resume_id INT NOT NULL
  , skill_group_id INT NULL
  , sort_order INT NOT NULL DEFAULT 0
  , is_visible BOOLEAN NOT NULL DEFAULT TRUE
  , name TEXT NOT NULL
  , icon_blob_id INT NULL
  , url TEXT NULL
  , first_used temporal.year_optional_month NULL
  , last_used temporal.year_optional_month NULL
  , versions TEXT NULL
  , rating SMALLINT NULL
  , is_highlight BOOLEAN NOT NULL DEFAULT FALSE
  , notes TEXT NULL
  , CONSTRAINT pk_skill PRIMARY KEY (id)
  , CONSTRAINT uq_skill_resume_name UNIQUE (resume_id, name)
  , CONSTRAINT fk_skill_resume FOREIGN KEY (resume_id)
      REFERENCES resume.resume (id) ON DELETE CASCADE
  , CONSTRAINT fk_skill_skill_group FOREIGN KEY (skill_group_id, resume_id)
      REFERENCES resume.skill_group (id, resume_id) ON DELETE SET NULL (skill_group_id)
  , CONSTRAINT fk_skill_icon_blob FOREIGN KEY (icon_blob_id)
      REFERENCES blob.blob (id)
  , CONSTRAINT ck_skill_name_not_empty CHECK (name != '')
  , CONSTRAINT ck_skill_rating_range CHECK (rating BETWEEN 0 AND 10)
  , CONSTRAINT ck_skill_last_used_not_before_first_used CHECK (first_used IS NULL OR last_used IS NULL OR left(last_used, 4) >= left(first_used, 4))
);

CREATE INDEX ix_skill_resume_id_sort_order ON resume.skill (resume_id, sort_order);

CREATE INDEX ix_skill_skill_group_id ON resume.skill (skill_group_id) WHERE skill_group_id IS NOT NULL;

CREATE INDEX ix_skill_icon_blob_id ON resume.skill (icon_blob_id) WHERE icon_blob_id IS NOT NULL;

CREATE TABLE resume.skill_tag (
    skill_id INT NOT NULL
  , tag_id INT NOT NULL
  , CONSTRAINT pk_skill_tag PRIMARY KEY (skill_id, tag_id)
  , CONSTRAINT fk_skill_tag_skill FOREIGN KEY (skill_id)
      REFERENCES resume.skill (id) ON DELETE CASCADE
  , CONSTRAINT fk_skill_tag_tag FOREIGN KEY (tag_id)
      REFERENCES tag.tag (id) ON DELETE CASCADE
);

CREATE INDEX ix_skill_tag_tag_id_skill_id ON resume.skill_tag (tag_id, skill_id);