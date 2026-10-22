CREATE SCHEMA form;

CREATE TABLE form.contact (
    contact_id       INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , name             text.trimmed  NOT NULL
  , email            text.email    NOT NULL
  , subject          text.trimmed      NULL
  , message          text.trimmed  NOT NULL
  , is_spam          BOOLEAN       NOT NULL DEFAULT FALSE
  , created_utc      TIMESTAMPTZ   NOT NULL DEFAULT now()
  , read_utc         TIMESTAMPTZ       NULL
  , notified_utc     TIMESTAMPTZ       NULL
  , notify_attempts  INT           NOT NULL DEFAULT 0
  , notify_error     text.trimmed      NULL

  , CONSTRAINT pk_contact PRIMARY KEY (contact_id)
  , CONSTRAINT ck_contact_message_length               CHECK (length(message) <= 10000)
  , CONSTRAINT ck_contact_notify_attempts_non_negative CHECK (notify_attempts >= 0)
  , CONSTRAINT ck_contact_read_not_before_created      CHECK (read_utc >= created_utc)
  , CONSTRAINT ck_contact_notified_not_before_created  CHECK (notified_utc >= created_utc)
);

CREATE INDEX ix_contact_created_utc
  ON form.contact (created_utc DESC);

CREATE INDEX ix_contact_pending_notification
  ON form.contact (created_utc)
  WHERE notified_utc IS NULL AND NOT is_spam;
