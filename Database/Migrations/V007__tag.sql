CREATE SCHEMA tag;

CREATE TABLE tag.tag (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , name TEXT NOT NULL
  , slug TEXT NOT NULL
  , description TEXT NULL
  , color TEXT NULL
  , CONSTRAINT pk_tag PRIMARY KEY (id)
  , CONSTRAINT uq_tag_slug UNIQUE (slug)
  , CONSTRAINT ck_tag_name_trimmed CHECK (name != '' AND name = btrim(name))
  , CONSTRAINT ck_tag_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
  , CONSTRAINT ck_tag_color_format CHECK (color ~ '^#[0-9a-f]{6}$')
);

CREATE UNIQUE INDEX ux_tag_name_lower ON tag.tag (lower(name));

CREATE TABLE tag.alias (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , tag_id INT NOT NULL
  , name TEXT NOT NULL
  , CONSTRAINT pk_alias PRIMARY KEY (id)
  , CONSTRAINT fk_alias_tag FOREIGN KEY (tag_id)
      REFERENCES tag.tag (id) ON DELETE CASCADE
  , CONSTRAINT ck_alias_name_trimmed CHECK (name != '' AND name = btrim(name))
);

CREATE UNIQUE INDEX ux_alias_name_lower ON tag.alias (lower(name));

CREATE INDEX ix_alias_tag_id ON tag.alias (tag_id);