CREATE SCHEMA blog;

CREATE TABLE blog.series (
    series_id    INT              NOT NULL GENERATED ALWAYS AS IDENTITY
  , slug         text.kebab_case  NOT NULL
  , title        text.trimmed     NOT NULL
  , description  text.trimmed         NULL
  , created_utc  TIMESTAMPTZ      NOT NULL DEFAULT now()

  , CONSTRAINT pk_series PRIMARY KEY (series_id)
  , CONSTRAINT uq_series_slug UNIQUE (slug)
);

CREATE TABLE blog.post (
    post_id        INT              NOT NULL GENERATED ALWAYS AS IDENTITY
  , slug           text.kebab_case  NOT NULL
  , title          text.trimmed     NOT NULL
  , summary        text.trimmed         NULL
  , body           text.trimmed     NOT NULL
  , cover_blob_id  INT                  NULL
  , series_id      INT                  NULL
  , series_order   INT                  NULL
  , canonical_url  text.url             NULL
  , is_featured    BOOLEAN          NOT NULL DEFAULT FALSE
  , published_utc  TIMESTAMPTZ          NULL
  , updated_utc    TIMESTAMPTZ          NULL
  , created_utc    TIMESTAMPTZ      NOT NULL DEFAULT now()

  , CONSTRAINT pk_post PRIMARY KEY (post_id)
  , CONSTRAINT uq_post_slug UNIQUE (slug)
  , CONSTRAINT uq_post_series_order UNIQUE (series_id, series_order) DEFERRABLE INITIALLY DEFERRED
  , CONSTRAINT fk_post__blob__blob FOREIGN KEY (cover_blob_id) REFERENCES blob.blob (blob_id)
  , CONSTRAINT fk_post__series     FOREIGN KEY (series_id)     REFERENCES blog.series (series_id)
  , CONSTRAINT ck_post_series_order_together        CHECK ((series_id IS NULL) = (series_order IS NULL))
  , CONSTRAINT ck_post_published_not_before_created CHECK (published_utc >= created_utc)
  , CONSTRAINT ck_post_updated_not_before_created   CHECK (updated_utc >= created_utc)
);

CREATE INDEX ix_post_published_utc
  ON blog.post (published_utc DESC)
  WHERE published_utc IS NOT NULL;

CREATE INDEX ix_post_cover_blob_id
  ON blog.post (cover_blob_id)
  WHERE cover_blob_id IS NOT NULL;

CREATE TABLE blog.post__tag__tag (
    post_id  INT  NOT NULL
  , tag_id   INT  NOT NULL

  , CONSTRAINT pk_post__tag__tag PRIMARY KEY (post_id, tag_id)
  , CONSTRAINT fk_post__tag__tag__post     FOREIGN KEY (post_id) REFERENCES blog.post (post_id) ON DELETE CASCADE
  , CONSTRAINT fk_post__tag__tag__tag__tag FOREIGN KEY (tag_id)  REFERENCES tag.tag (tag_id)    ON DELETE CASCADE
);

CREATE INDEX ix_post__tag__tag_tag_id_post_id
  ON blog.post__tag__tag (tag_id, post_id);