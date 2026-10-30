CREATE SCHEMA auth;

CREATE TABLE auth.user (
    user_id       INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , display_name  text.trimmed  NOT NULL
  , is_active     BOOLEAN       NOT NULL DEFAULT TRUE
  , created_utc   TIMESTAMPTZ   NOT NULL DEFAULT now()

  , CONSTRAINT pk_user PRIMARY KEY (user_id)
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

CREATE TABLE auth.identity (
    identity_id    INT           NOT NULL GENERATED ALWAYS AS IDENTITY
  , user_id        INT           NOT NULL
  , provider       text.trimmed  NOT NULL
  , subject        text.trimmed  NOT NULL
  , password_hash  text.trimmed      NULL
  , data           jsonb.object  NOT NULL DEFAULT '{}'
  , created_utc    TIMESTAMPTZ   NOT NULL DEFAULT now()
  , last_used_utc  TIMESTAMPTZ       NULL

  , CONSTRAINT pk_identity PRIMARY KEY (identity_id)
  , CONSTRAINT uq_identity_provider_subject UNIQUE (provider, subject)
  , CONSTRAINT fk_identity__user FOREIGN KEY (user_id) REFERENCES auth.user (user_id) ON DELETE CASCADE
  , CONSTRAINT ck_identity_provider_lowercase           CHECK (provider = lower(provider))
  , CONSTRAINT ck_identity_password_requires_hash       CHECK (provider != 'password' OR password_hash IS NOT NULL)
  , CONSTRAINT ck_identity_last_used_not_before_created CHECK (last_used_utc >= created_utc)
);

CREATE INDEX ix_identity_user_id
  ON auth.identity (user_id);

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
