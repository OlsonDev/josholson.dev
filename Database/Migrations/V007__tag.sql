CREATE SCHEMA tag;

CREATE TABLE tag.tag (
    tag_id       INT              NOT NULL GENERATED ALWAYS AS IDENTITY
  , name         text.trimmed     NOT NULL
  , slug         text.kebab_case  NOT NULL
  , description  text.trimmed         NULL
  , color        color.hex            NULL

  , CONSTRAINT pk_tag PRIMARY KEY (tag_id)
  , CONSTRAINT uq_tag_slug UNIQUE (slug)
);

CREATE UNIQUE INDEX ux_tag_name_lower
  ON tag.tag (lower(name));

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
