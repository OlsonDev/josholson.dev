CREATE SCHEMA blob;

CREATE TABLE blob.mime_type (
    mime_type_id  INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , name          text.trimmed  NOT NULL
  , is_image      BOOLEAN       NOT NULL DEFAULT FALSE

  , CONSTRAINT pk_mime_type PRIMARY KEY (mime_type_id)
  , CONSTRAINT uq_mime_type_name UNIQUE (name)
  , CONSTRAINT ck_mime_type_name_lowercase CHECK (name = lower(name))
);

CREATE TABLE blob.file_extension (
    file_extension_id  INT   NOT NULL GENERATED ALWAYS AS IDENTITY
  , name               TEXT  NOT NULL

  , CONSTRAINT pk_file_extension PRIMARY KEY (file_extension_id)
  , CONSTRAINT uq_file_extension_name UNIQUE (name)
  , CONSTRAINT ck_file_extension_name_format CHECK (name ~ '^[a-z0-9]+$')
);

CREATE TABLE blob.mime_type__file_extension (
    mime_type_id       INT      NOT NULL
  , file_extension_id  INT      NOT NULL
  , is_preferred       BOOLEAN  NOT NULL DEFAULT FALSE

  , CONSTRAINT pk_mime_type__file_extension PRIMARY KEY (mime_type_id, file_extension_id)
  , CONSTRAINT fk_mime_type__file_extension__mime_type      FOREIGN KEY (mime_type_id)      REFERENCES blob.mime_type (mime_type_id)           ON DELETE CASCADE
  , CONSTRAINT fk_mime_type__file_extension__file_extension FOREIGN KEY (file_extension_id) REFERENCES blob.file_extension (file_extension_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ux_mime_type__file_extension_one_preferred
  ON blob.mime_type__file_extension (mime_type_id)
  WHERE is_preferred;

CREATE INDEX ix_mime_type__file_extension_file_extension_id
  ON blob.mime_type__file_extension (file_extension_id);

CREATE TABLE blob.blob (
    blob_id             INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , sha256              hash.sha256   NOT NULL
  , mime_type_id        INT           NOT NULL
  , byte_size           BIGINT        NOT NULL
  , width               INT               NULL
  , height              INT               NULL
  , thumbhash           BYTEA             NULL
  , original_file_name  text.trimmed      NULL
  , created_utc         TIMESTAMPTZ   NOT NULL DEFAULT now()

  , CONSTRAINT pk_blob PRIMARY KEY (blob_id)
  , CONSTRAINT uq_blob_sha256 UNIQUE (sha256)
  , CONSTRAINT fk_blob__mime_type FOREIGN KEY (mime_type_id) REFERENCES blob.mime_type (mime_type_id)
  , CONSTRAINT ck_blob_byte_size_non_negative CHECK (byte_size >= 0)
  , CONSTRAINT ck_blob_width_positive         CHECK (width > 0)
  , CONSTRAINT ck_blob_height_positive        CHECK (height > 0)
  , CONSTRAINT ck_blob_dimensions_together    CHECK ((width IS NULL) = (height IS NULL))
);

CREATE INDEX ix_blob_mime_type_id
  ON blob.blob (mime_type_id);
