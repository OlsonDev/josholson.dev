CREATE SCHEMA blog;

CREATE TABLE blog.series (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , slug TEXT NOT NULL
  , title TEXT NOT NULL
  , description TEXT NULL
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , CONSTRAINT pk_series PRIMARY KEY (id)
  , CONSTRAINT uq_series_slug UNIQUE (slug)
  , CONSTRAINT ck_series_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
  , CONSTRAINT ck_series_title_not_empty CHECK (title != '')
);

CREATE TABLE blog.post (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , slug TEXT NOT NULL
  , title TEXT NOT NULL
  , summary TEXT NULL
  , body TEXT NOT NULL
  , cover_blob_id INT NULL
  , series_id INT NULL
  , series_order INT NULL
  , canonical_url TEXT NULL
  , is_featured BOOLEAN NOT NULL DEFAULT FALSE
  , published_utc TIMESTAMPTZ NULL
  , updated_utc TIMESTAMPTZ NULL
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , CONSTRAINT pk_post PRIMARY KEY (id)
  , CONSTRAINT uq_post_slug UNIQUE (slug)
  , CONSTRAINT uq_post_series_order UNIQUE (series_id, series_order) DEFERRABLE INITIALLY DEFERRED
  , CONSTRAINT fk_post_cover_blob FOREIGN KEY (cover_blob_id)
      REFERENCES blob.blob (id)
  , CONSTRAINT fk_post_series FOREIGN KEY (series_id)
      REFERENCES blog.series (id)
  , CONSTRAINT ck_post_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
  , CONSTRAINT ck_post_title_not_empty CHECK (title != '')
  , CONSTRAINT ck_post_series_order_together CHECK ((series_id IS NULL) = (series_order IS NULL))
);

CREATE INDEX ix_post_published_utc ON blog.post (published_utc DESC) WHERE published_utc IS NOT NULL;

CREATE INDEX ix_post_cover_blob_id ON blog.post (cover_blob_id) WHERE cover_blob_id IS NOT NULL;

CREATE TABLE blog.post_tag (
    post_id INT NOT NULL
  , tag_id INT NOT NULL
  , CONSTRAINT pk_post_tag PRIMARY KEY (post_id, tag_id)
  , CONSTRAINT fk_post_tag_post FOREIGN KEY (post_id)
      REFERENCES blog.post (id) ON DELETE CASCADE
  , CONSTRAINT fk_post_tag_tag FOREIGN KEY (tag_id)
      REFERENCES tag.tag (id) ON DELETE CASCADE
);

CREATE INDEX ix_post_tag_tag_id_post_id ON blog.post_tag (tag_id, post_id);