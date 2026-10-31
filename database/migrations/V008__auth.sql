CREATE SCHEMA auth;

CREATE TABLE auth.user (
    user_id       INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , display_name  text.trimmed  NOT NULL
  , user_handle   BYTEA         NOT NULL
  , is_active     BOOLEAN       NOT NULL DEFAULT TRUE
  , created_utc   TIMESTAMPTZ   NOT NULL DEFAULT now()

  , CONSTRAINT pk_user PRIMARY KEY (user_id)
  , CONSTRAINT uq_user_user_handle UNIQUE (user_handle)
  , CONSTRAINT ck_user_user_handle_length CHECK (octet_length(user_handle) BETWEEN 16 AND 64)
);

CREATE TABLE auth.user_email (
    user_email_id  INT          NOT NULL GENERATED ALWAYS AS IDENTITY
  , user_id        INT          NOT NULL
  , email          text.email   NOT NULL
  , is_primary     BOOLEAN      NOT NULL DEFAULT FALSE
  , verified_utc   TIMESTAMPTZ      NULL
  , created_utc    TIMESTAMPTZ  NOT NULL DEFAULT now()

  , CONSTRAINT pk_user_email PRIMARY KEY (user_email_id)
  , CONSTRAINT fk_user_email__user FOREIGN KEY (user_id) REFERENCES auth.user (user_id) ON DELETE CASCADE
  , CONSTRAINT ck_user_email_verified_not_before_created CHECK (verified_utc >= created_utc)
);

CREATE UNIQUE INDEX ux_user_email_email_lower
  ON auth.user_email (lower(email));

CREATE UNIQUE INDEX ux_user_email_one_primary_per_user
  ON auth.user_email (user_id)
  WHERE is_primary;

CREATE INDEX ix_user_email_user_id
  ON auth.user_email (user_id);

CREATE TABLE auth.passkey (
    passkey_id          INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , user_id             INT           NOT NULL
  , credential_id       BYTEA         NOT NULL
  , public_key          BYTEA         NOT NULL
  , sign_count          BIGINT        NOT NULL DEFAULT 0
  , transports          SMALLINT[]    NOT NULL DEFAULT '{}'
  , is_backup_eligible  BOOLEAN       NOT NULL
  , is_backed_up        BOOLEAN       NOT NULL
  , nickname            text.trimmed  NOT NULL
  , created_utc         TIMESTAMPTZ   NOT NULL DEFAULT now()
  , last_used_utc       TIMESTAMPTZ       NULL

  , CONSTRAINT pk_passkey PRIMARY KEY (passkey_id)
  , CONSTRAINT uq_passkey_credential_id UNIQUE (credential_id)
  , CONSTRAINT uq_passkey_user_id_nickname UNIQUE (user_id, nickname)
  , CONSTRAINT fk_passkey__user FOREIGN KEY (user_id) REFERENCES auth.user (user_id) ON DELETE CASCADE
  , CONSTRAINT ck_passkey_credential_id_length         CHECK (octet_length(credential_id) BETWEEN 16 AND 1023)
  , CONSTRAINT ck_passkey_sign_count_range             CHECK (sign_count BETWEEN 0 AND 4294967295)
  , CONSTRAINT ck_passkey_transports_range             CHECK (transports <@ ARRAY[1, 2, 3, 4, 5, 6]::SMALLINT[])
  , CONSTRAINT ck_passkey_transports_no_nulls          CHECK (array_position(transports, NULL) IS NULL)
  , CONSTRAINT ck_passkey_backed_up_requires_eligible  CHECK (NOT is_backed_up OR is_backup_eligible)
  , CONSTRAINT ck_passkey_last_used_not_before_created CHECK (last_used_utc >= created_utc)
);

CREATE TABLE auth.session (
    session_id     INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , user_id        INT           NOT NULL
  , token_hash     hash.sha256   NOT NULL
  , created_utc    TIMESTAMPTZ   NOT NULL DEFAULT now()
  , expires_utc    TIMESTAMPTZ   NOT NULL
  , last_seen_utc  TIMESTAMPTZ   NOT NULL DEFAULT now()
  , ip             INET              NULL
  , user_agent     text.trimmed      NULL

  , CONSTRAINT pk_session PRIMARY KEY (session_id)
  , CONSTRAINT uq_session_token_hash UNIQUE (token_hash)
  , CONSTRAINT fk_session__user FOREIGN KEY (user_id) REFERENCES auth.user (user_id) ON DELETE CASCADE
  , CONSTRAINT ck_session_expires_after_created        CHECK (expires_utc > created_utc)
  , CONSTRAINT ck_session_last_seen_not_before_created CHECK (last_seen_utc >= created_utc)
);

CREATE INDEX ix_session_user_id
  ON auth.session (user_id);

CREATE INDEX ix_session_expires_utc
  ON auth.session (expires_utc);
