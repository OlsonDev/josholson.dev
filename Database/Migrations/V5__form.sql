CREATE SCHEMA form;

CREATE TABLE form.contact (
    id INT NOT NULL GENERATED ALWAYS AS IDENTITY
  , name TEXT NOT NULL
  , email TEXT NOT NULL
  , subject TEXT NULL
  , message TEXT NOT NULL
  , is_spam BOOLEAN NOT NULL DEFAULT FALSE
  , created_utc TIMESTAMPTZ NOT NULL DEFAULT now()
  , read_utc TIMESTAMPTZ NULL
  , notified_utc TIMESTAMPTZ NULL
  , notify_attempts INT NOT NULL DEFAULT 0
  , notify_error TEXT NULL
  , CONSTRAINT pk_contact PRIMARY KEY (id)
  , CONSTRAINT ck_contact_name_not_empty CHECK (name != '')
  , CONSTRAINT ck_contact_email_format CHECK (position('@' IN email) > 1)
  , CONSTRAINT ck_contact_message_length CHECK (message != '' AND length(message) <= 10000)
);

CREATE INDEX ix_contact_created_utc ON form.contact (created_utc DESC);

CREATE INDEX ix_contact_pending_notification
  ON form.contact (created_utc) WHERE notified_utc IS NULL AND NOT is_spam;