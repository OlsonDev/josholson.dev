CREATE SCHEMA tag;

CREATE TABLE tag.tag (
    tag_id            INT             NOT NULL GENERATED ALWAYS AS IDENTITY
  , name              text.trimmed    NOT NULL
  , description       text.trimmed        NULL
  , description_html  TEXT                NULL
  , color             color.tailwind      NULL
  , version           INT             NOT NULL DEFAULT 1

  , CONSTRAINT pk_tag PRIMARY KEY (tag_id)
  , CONSTRAINT ck_tag_description_html_together CHECK ((description IS NULL) = (description_html IS NULL))
);

CREATE UNIQUE INDEX ux_tag_name_lower
  ON tag.tag (lower(name));

CREATE TABLE tag.slug (
    slug_id       INT              NOT NULL GENERATED ALWAYS AS IDENTITY
  , tag_id        INT              NOT NULL
  , slug          text.kebab_case  NOT NULL
  , is_canonical  BOOLEAN          NOT NULL DEFAULT FALSE
  , created_utc   TIMESTAMPTZ      NOT NULL DEFAULT now()

  , CONSTRAINT pk_slug PRIMARY KEY (slug_id)
  , CONSTRAINT uq_slug_slug UNIQUE (slug)
  , CONSTRAINT fk_slug__tag FOREIGN KEY (tag_id) REFERENCES tag.tag (tag_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ux_slug_one_canonical_per_tag
  ON tag.slug (tag_id)
  WHERE is_canonical;

CREATE INDEX ix_slug_tag_id
  ON tag.slug (tag_id);

CREATE TABLE tag.alias (
    alias_id  INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , tag_id    INT           NOT NULL
  , name      text.trimmed  NOT NULL

  , CONSTRAINT pk_alias PRIMARY KEY (alias_id)
  , CONSTRAINT fk_alias__tag FOREIGN KEY (tag_id) REFERENCES tag.tag (tag_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ux_alias_name_lower
  ON tag.alias (lower(name));

CREATE INDEX ix_alias_tag_id
  ON tag.alias (tag_id);
